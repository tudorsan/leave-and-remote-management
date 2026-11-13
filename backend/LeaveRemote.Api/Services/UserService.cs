using LeaveRemote.Api.Controllers;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>
/// Administrarea utilizatorilor: rol, echipă și manager sunt atribuite exclusiv de Admin.
/// Conturile se dezactivează (fără ștergere) și pot fi reactivate.
/// </summary>
public sealed class UserService(AppDbContext db, SettingsService settings, AppClock clock)
{
    private const int MinPasswordLength = 8;
    private static readonly ServiceError NotFound = ServiceError.NotFound("USER_NOT_FOUND", "Utilizatorul nu a fost găsit.");

    /// <summary>Adminul vede toți utilizatorii; Managerul, echipa sa și subordonații direcți.</summary>
    public async Task<Result<List<UserView>>> GetVisibleAsync(User viewer)
    {
        var query = db.Users.AsNoTracking();
        if (viewer.Role == UserRole.Manager)
        {
            if (!viewer.TeamId.HasValue)
            {
                return ServiceError.Forbidden("MANAGER_WITHOUT_TEAM", "Managerul nu are o echipă atribuită.");
            }
            query = query.Where(u => u.TeamId == viewer.TeamId.Value || u.ManagerId == viewer.Id);
        }
        else if (viewer.Role != UserRole.Admin)
        {
            return ServiceError.Forbidden();
        }

        return await query.OrderBy(u => u.Id)
            .Select(u => new UserView(u.Id, u.FirstName, u.LastName, u.Email, u.Role.ToString(), u.TeamId, u.ManagerId, u.IsActive))
            .ToListAsync();
    }

    public async Task<Result<UserView>> CreateAsync(CreateUserInput input)
    {
        var firstName = input.FirstName?.Trim();
        var lastName = input.LastName?.Trim();
        var email = input.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
            string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(input.Password))
        {
            return ServiceError.BadRequest("INVALID_USER_DATA", "Numele, prenumele, emailul și parola sunt obligatorii.");
        }
        if (!IsValidEmail(email)) return InvalidEmail;
        if (ValidatePassword(input.Password) is { } passwordError) return passwordError;
        if (!ApiInput.TryParseEnumName<UserRole>(input.Role, out var role)) return InvalidRole;
        if (await EmailTakenAsync(email, null)) return EmailTaken;
        if (await ValidateHierarchyAsync(role, input.TeamId, input.ManagerId, null) is { } hierarchyError) return hierarchyError;

        // Maximul anual al angajatului pornește de la valoarea implicită globală (cerința 17).
        var defaultDays = await settings.GetDefaultAnnualDaysAsync();
        if (defaultDays.Error is not null) return defaultDays.Error;

        var user = new User
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(input.Password),
            Role = role,
            IsActive = true,
            TeamId = input.TeamId,
            ManagerId = input.ManagerId,
            DefaultAnnualLeaveDays = defaultDays.Value
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.AnnualLeaveBalances.Add(new AnnualLeaveBalance
        {
            UserId = user.Id, Year = clock.CurrentYear, TotalDays = defaultDays.Value
        });
        await db.SaveChangesAsync();
        return ToView(user);
    }

    public async Task<Result<UserView>> UpdateAsync(User admin, int id, UpdateUserInput input)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound;

        var firstName = input.FirstName?.Trim();
        var lastName = input.LastName?.Trim();
        var email = input.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(email))
        {
            return ServiceError.BadRequest("INVALID_USER_DATA", "Numele, prenumele și emailul sunt obligatorii.");
        }
        if (!IsValidEmail(email)) return InvalidEmail;

        var role = user.Role;
        if (input.Role != null && !ApiInput.TryParseEnumName(input.Role, out role)) return InvalidRole;
        if (await EmailTakenAsync(email, id)) return EmailTaken;

        if (admin.Id == id && role != UserRole.Admin)
        {
            return ServiceError.BadRequest("CANNOT_REMOVE_OWN_ADMIN_ROLE", "Nu îți poți elimina propriul rol de Admin.");
        }

        if (await ValidateHierarchyAsync(role, input.TeamId, input.ManagerId, id) is { } hierarchyError) return hierarchyError;

        // Un superior cu subordonați activi nu poate primi un rol sau o echipă incompatibilă cu ei.
        var subordinates = await db.Users.AsNoTracking().Where(u => u.ManagerId == id && u.IsActive).ToListAsync();
        if (subordinates.Count > 0)
        {
            if (role == UserRole.Employee)
            {
                return ServiceError.Conflict("MANAGER_HAS_SUBORDINATES",
                    "Utilizatorul are subordonați activi. Reatribuie-i înainte de schimbarea rolului.");
            }
            if (role == UserRole.Manager && subordinates.Any(u => u.Role == UserRole.Employee && u.TeamId != input.TeamId))
            {
                return ServiceError.Conflict("SUBORDINATE_TEAM_MISMATCH",
                    "Există angajați subordonați din altă echipă. Reatribuie-i înainte de modificare.");
            }
        }

        if (await CreatesCycleAsync(id, input.ManagerId))
        {
            return ServiceError.Conflict("MANAGER_HIERARCHY_CYCLE", "Ierarhia managerilor nu poate conține cicluri.");
        }

        user.FirstName = firstName;
        user.LastName = lastName;
        user.Email = email;
        user.Role = role;
        user.TeamId = input.TeamId;
        user.ManagerId = input.ManagerId;
        await db.SaveChangesAsync();
        return ToView(user);
    }

    /// <summary>
    /// Dezactivare fără ștergere: contul nu se mai poate autentifica (nici cu un token deja emis),
    /// iar cererile lui în așteptare sunt anulate ca să nu rămână blocate și să elibereze soldul.
    /// </summary>
    public async Task<Result<UserView>> DeactivateAsync(User admin, int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound;
        if (!user.IsActive) return ServiceError.Conflict("USER_ALREADY_INACTIVE", "Utilizatorul este deja dezactivat.");
        if (admin.Id == id) return ServiceError.BadRequest("CANNOT_DEACTIVATE_SELF", "Nu îți poți dezactiva propriul cont.");
        if (await db.Users.AnyAsync(u => u.ManagerId == id && u.IsActive))
        {
            return ServiceError.Conflict("MANAGER_HAS_SUBORDINATES",
                "Reatribuie subordonații activi înainte de dezactivarea acestui utilizator.");
        }

        user.IsActive = false;
        await db.SaveChangesAsync();

        await db.LeaveRequests.Where(r => r.UserId == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.Canceled));
        await db.RemoteRequests.Where(r => r.UserId == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.Canceled));
        return ToView(user);
    }

    public async Task<Result<UserView>> ReactivateAsync(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound;
        if (user.IsActive) return ServiceError.Conflict("USER_ALREADY_ACTIVE", "Utilizatorul este deja activ.");

        if (await ValidateHierarchyAsync(user.Role, user.TeamId, user.ManagerId, id) is not null)
        {
            return ServiceError.Conflict("REACTIVATION_HIERARCHY_INVALID",
                "Echipa sau managerul utilizatorului nu mai sunt active. Actualizează fișa utilizatorului înainte de reactivare.");
        }

        user.IsActive = true;
        await db.SaveChangesAsync();
        return ToView(user);
    }

    private async Task<ServiceError?> ValidateHierarchyAsync(UserRole role, int? teamId, int? managerId, int? userId)
    {
        if (role == UserRole.Manager && (!teamId.HasValue || !managerId.HasValue))
        {
            return ServiceError.BadRequest("MANAGER_HIERARCHY_REQUIRED", "Un Manager trebuie să aibă o echipă și un superior atribuit.");
        }
        if (role == UserRole.Employee && !teamId.HasValue)
        {
            return ServiceError.BadRequest("EMPLOYEE_TEAM_REQUIRED", "Un angajat trebuie să aparțină unei echipe.");
        }
        if (teamId.HasValue && !await db.Teams.AnyAsync(t => t.Id == teamId.Value && t.IsActive))
        {
            return ServiceError.BadRequest("INVALID_TEAM", "Echipa nu există sau este dezactivată.");
        }
        if (!managerId.HasValue) return null;

        if (userId.HasValue && managerId.Value == userId.Value)
        {
            return ServiceError.BadRequest("INVALID_MANAGER", "Un utilizator nu poate fi propriul manager.");
        }

        var manager = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == managerId.Value && u.IsActive);
        if (manager is null || manager.Role is not (UserRole.Manager or UserRole.Admin))
        {
            return ServiceError.BadRequest("INVALID_MANAGER", "Superiorul trebuie să fie un Manager sau Admin activ.");
        }
        if (role == UserRole.Employee && manager.Role == UserRole.Manager && manager.TeamId != teamId)
        {
            return ServiceError.BadRequest("MANAGER_TEAM_MISMATCH", "Managerul direct al angajatului trebuie să aparțină aceleiași echipe.");
        }
        return null;
    }

    /// <summary>Previne ciclurile de tip A → B → A, inclusiv lanțurile mai lungi.</summary>
    private async Task<bool> CreatesCycleAsync(int userId, int? managerId)
    {
        var visited = new HashSet<int> { userId };
        var next = managerId;
        while (next.HasValue)
        {
            if (!visited.Add(next.Value)) return true;
            next = await db.Users.AsNoTracking().Where(u => u.Id == next.Value).Select(u => u.ManagerId).FirstOrDefaultAsync();
        }
        return false;
    }

    private static ServiceError? ValidatePassword(string password)
    {
        if (password.Length < MinPasswordLength)
        {
            return ServiceError.BadRequest("INVALID_PASSWORD", $"Parola trebuie să conțină minimum {MinPasswordLength} caractere.");
        }
        return ApiInput.FitsBCrypt(password)
            ? null
            : ServiceError.BadRequest("INVALID_PASSWORD",
                $"Parola poate avea maximum {ApiInput.BCryptMaxPasswordBytes} de octeți în UTF-8 și nu poate conține caracterul nul.");
    }

    private Task<bool> EmailTakenAsync(string email, int? excludeId) =>
        db.Users.AnyAsync(u => u.Email.ToLower() == email && (!excludeId.HasValue || u.Id != excludeId.Value));

    private static bool IsValidEmail(string email) =>
        email.Length <= 450 && System.Net.Mail.MailAddress.TryCreate(email, out var address) && address.Address == email;

    private static UserView ToView(User u) =>
        new(u.Id, u.FirstName, u.LastName, u.Email, u.Role.ToString(), u.TeamId, u.ManagerId, u.IsActive);

    private static readonly ServiceError InvalidEmail = ServiceError.BadRequest("INVALID_EMAIL", "Adresa de email este invalidă.");
    private static readonly ServiceError InvalidRole = ServiceError.BadRequest("INVALID_ROLE", "Rolul trebuie să fie Employee, Manager sau Admin.");
    private static readonly ServiceError EmailTaken = ServiceError.Conflict("EMAIL_ALREADY_EXISTS", "Există deja un utilizator cu această adresă de email.");
}
