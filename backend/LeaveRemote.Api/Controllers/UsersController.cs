
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LeaveRemote.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Manager,Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/users
    // Manager: utilizatorii din echipa sa.
    // Admin: toti utilizatorii.
    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return InvalidToken();
        }

        var currentUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u =>
                u.Id == currentUserId && u.IsActive);

        if (currentUser == null)
        {
            return InactiveUser();
        }

        if (currentUser.Role != UserRole.Manager &&
            currentUser.Role != UserRole.Admin)
        {
            return ApiErrors.Forbidden();
        }

        var query = _db.Users.AsNoTracking();

        if (currentUser.Role == UserRole.Manager)
        {
            if (!currentUser.TeamId.HasValue)
            {
                return StatusCode(403, new
                {
                    code = "MANAGER_WITHOUT_TEAM",
                    message = "Managerul nu are o echipa atribuita."
                });
            }

            query = query.Where(u =>
                u.TeamId == currentUser.TeamId.Value || u.ManagerId == currentUser.Id);
        }

        var users = await query
            .OrderBy(u => u.Id)
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                Role = u.Role.ToString(),
                u.TeamId,
                u.ManagerId,
                u.IsActive
            })
            .ToListAsync();

        return Ok(users);
    }

    // POST /api/users
    // Doar Admin poate crea utilizatori si atribui roluri.
    [HttpPost]
    [RequestWriteTransaction]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserRequest request)
    {
        var authorizationError = await ValidateCurrentAdminAsync();

        if (authorizationError != null)
        {
            return authorizationError;
        }

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(lastName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                code = "INVALID_USER_DATA",
                message = "Numele, prenumele, emailul si parola sunt obligatorii."
            });
        }

        if (!IsValidEmail(email))
        {
            return BadRequest(new
            {
                code = "INVALID_EMAIL",
                message = "Adresa de email este invalida."
            });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new
            {
                code = "INVALID_PASSWORD",
                message = "Parola trebuie sa contina minimum 8 caractere."
            });
        }

        if (!ApiInput.FitsBCrypt(request.Password))
        {
            return BadRequest(new
            {
                code = "INVALID_PASSWORD",
                message = $"Parola poate avea maximum {ApiInput.BCryptMaxPasswordBytes} de octeți în UTF-8 și nu poate conține caracterul nul."
            });
        }

        if (!TryParseRole(request.Role, out var role))
        {
            return InvalidRole();
        }

        var emailExists = await _db.Users.AnyAsync(u =>
            u.Email.ToLower() == email);

        if (emailExists)
        {
            return Conflict(new
            {
                code = "EMAIL_ALREADY_EXISTS",
                message = "Exista deja un utilizator cu aceasta adresa de email."
            });
        }

        var hierarchyError = await ValidateHierarchyAsync(
            role,
            request.TeamId,
            request.ManagerId);

        if (hierarchyError != null)
        {
            return hierarchyError;
        }

        var user = new User
        {
            FirstName = firstName!,
            LastName = lastName!,
            Email = email!,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(
                request.Password),
            Role = role,
            IsActive = true,
            TeamId = request.TeamId,
            ManagerId = request.ManagerId
        };

        // Soldul initial este citit din setarea globala, nu hardcodat.
        var defaultDaysText = await _db.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == "leave.default_annual_days")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();

        if (!int.TryParse(defaultDaysText, out var defaultDays) ||
            defaultDays < 0)
        {
            return StatusCode(500, new
            {
                code = "INVALID_DEFAULT_ANNUAL_DAYS_SETTING",
                message = "Setarea leave.default_annual_days lipseste sau este invalida."
            });
        }

        user.DefaultAnnualLeaveDays = defaultDays;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _db.AnnualLeaveBalances.Add(new AnnualLeaveBalance
        {
            UserId = user.Id,
            Year = DateTime.Today.Year,
            TotalDays = defaultDays
        });

        await _db.SaveChangesAsync();

        return Created($"/api/users/{user.Id}", new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            Role = user.Role.ToString(),
            user.TeamId,
            user.ManagerId,
            user.IsActive
        });
    }

    // PUT /api/users/{id}
    // Doar Admin poate modifica utilizatori si roluri.
    [HttpPut("{id:int}")]
    [RequestWriteTransaction]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateUser(
        int id,
        [FromBody] UpdateUserRequest request)
    {
        var authorizationError = await ValidateCurrentAdminAsync();

        if (authorizationError != null)
        {
            return authorizationError;
        }

        var user = await _db.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound(new
            {
                code = "USER_NOT_FOUND",
                message = "Utilizatorul nu a fost gasit."
            });
        }

        var firstName = request.FirstName?.Trim();
        var lastName = request.LastName?.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(firstName) ||
            string.IsNullOrWhiteSpace(lastName) ||
            string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new
            {
                code = "INVALID_USER_DATA",
                message = "Numele, prenumele si emailul sunt obligatorii."
            });
        }

        if (!IsValidEmail(email))
        {
            return BadRequest(new
            {
                code = "INVALID_EMAIL",
                message = "Adresa de email este invalida."
            });
        }

        var role = user.Role;
        if (request.Role != null && !TryParseRole(request.Role, out role))
        {
            return InvalidRole();
        }

        var emailExists = await _db.Users.AnyAsync(u =>
            u.Id != id && u.Email.ToLower() == email);

        if (emailExists)
        {
            return Conflict(new
            {
                code = "EMAIL_ALREADY_EXISTS",
                message = "Exista deja un utilizator cu aceasta adresa de email."
            });
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return InvalidToken();
        }

        if (currentUserId == id && role != UserRole.Admin)
        {
            return BadRequest(new
            {
                code = "CANNOT_REMOVE_OWN_ADMIN_ROLE",
                message = "Nu iti poti elimina propriul rol de Admin."
            });
        }

        var hierarchyError = await ValidateHierarchyAsync(
            role,
            request.TeamId,
            request.ManagerId,
            id);

        if (hierarchyError != null)
        {
            return hierarchyError;
        }

        // Un superior activ nu poate fi schimbat intr-un rol
        // incompatibil daca are subordonati activi.
        var activeSubordinates = await _db.Users
            .AsNoTracking()
            .Where(u => u.ManagerId == id && u.IsActive)
            .ToListAsync();

        if (activeSubordinates.Count > 0)
        {
            if (role == UserRole.Employee)
            {
                return Conflict(new
                {
                    code = "MANAGER_HAS_SUBORDINATES",
                    message = "Utilizatorul are subordonati activi. Reatribuie-i inainte de schimbarea rolului."
                });
            }

            if (role == UserRole.Manager)
            {
                var incompatibleSubordinate = activeSubordinates
                    .Any(u =>
                        u.Role == UserRole.Employee &&
                        u.TeamId != request.TeamId);

                if (incompatibleSubordinate)
                {
                    return Conflict(new
                    {
                        code = "SUBORDINATE_TEAM_MISMATCH",
                        message = "Exista angajati subordonati din alta echipa. Reatribuie-i inainte de modificare."
                    });
                }
            }
        }

        // Prevenim ciclurile de tip A -> B -> A,
        // inclusiv lanturile de manageri mai lungi.
        if (request.ManagerId.HasValue)
        {
            var visited = new HashSet<int> { id };
            int? nextManagerId = request.ManagerId;

            while (nextManagerId.HasValue)
            {
                if (!visited.Add(nextManagerId.Value))
                {
                    return Conflict(new
                    {
                        code = "MANAGER_HIERARCHY_CYCLE",
                        message = "Ierarhia managerilor nu poate contine cicluri."
                    });
                }

                nextManagerId = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == nextManagerId.Value)
                    .Select(u => u.ManagerId)
                    .FirstOrDefaultAsync();
            }
        }

        user.FirstName = firstName!;
        user.LastName = lastName!;
        user.Email = email!;
        user.Role = role;
        user.TeamId = request.TeamId;
        user.ManagerId = request.ManagerId;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            Role = user.Role.ToString(),
            user.TeamId,
            user.ManagerId,
            user.IsActive
        });
    }

    // PATCH /api/users/{id}/deactivate
    [HttpPatch("{id:int}/deactivate")]
    [RequestWriteTransaction]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeactivateUser(int id)
    {
        var authorizationError = await ValidateCurrentAdminAsync();

        if (authorizationError != null)
        {
            return authorizationError;
        }

        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return InvalidToken();
        }

        var user = await _db.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound(new
            {
                code = "USER_NOT_FOUND",
                message = "Utilizatorul nu a fost gasit."
            });
        }

        if (!user.IsActive)
        {
            return Conflict(new
            {
                code = "USER_ALREADY_INACTIVE",
                message = "Utilizatorul este deja dezactivat."
            });
        }

        if (currentUserId == id)
        {
            return BadRequest(new
            {
                code = "CANNOT_DEACTIVATE_SELF",
                message = "Nu iti poti dezactiva propriul cont."
            });
        }

        var hasActiveSubordinates = await _db.Users
            .AnyAsync(u => u.ManagerId == id && u.IsActive);

        if (hasActiveSubordinates)
        {
            return Conflict(new
            {
                code = "MANAGER_HAS_SUBORDINATES",
                message = "Reatribuie subordonatii activi inainte de dezactivarea acestui utilizator."
            });
        }

        user.IsActive = false;
        await _db.SaveChangesAsync();

        // Cererile în așteptare ale unui cont dezactivat nu mai pot fi urmărite de titular:
        // le anulăm, ca să nu rămână blocate și să elibereze soldul rezervat.
        await _db.LeaveRequests
            .Where(r => r.UserId == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.Canceled));
        await _db.RemoteRequests
            .Where(r => r.UserId == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.Canceled));

        return Ok(new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.IsActive
        });
    }

    // PATCH /api/users/{id}/reactivate
    [HttpPatch("{id:int}/reactivate")]
    [RequestWriteTransaction]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ReactivateUser(int id)
    {
        var authorizationError = await ValidateCurrentAdminAsync();
        if (authorizationError != null) return authorizationError;

        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            return NotFound(new { code = "USER_NOT_FOUND", message = "Utilizatorul nu a fost gasit." });
        }
        if (user.IsActive)
        {
            return Conflict(new { code = "USER_ALREADY_ACTIVE", message = "Utilizatorul este deja activ." });
        }

        // Echipa și managerul trebuie să fie încă valide (active) la reactivare.
        var hierarchyError = await ValidateHierarchyAsync(user.Role, user.TeamId, user.ManagerId, id);
        if (hierarchyError != null)
        {
            return Conflict(new
            {
                code = "REACTIVATION_HIERARCHY_INVALID",
                message = "Echipa sau managerul utilizatorului nu mai sunt active. Actualizează fișa utilizatorului înainte de reactivare."
            });
        }

        user.IsActive = true;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.IsActive
        });
    }

    // Verifica rolul Admin direct in baza de date,
    // nu doar in tokenul JWT.
    private async Task<IActionResult?> ValidateCurrentAdminAsync()
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return InvalidToken();
        }

        var currentUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u =>
                u.Id == currentUserId && u.IsActive);

        if (currentUser == null)
        {
            return InactiveUser();
        }

        if (currentUser.Role != UserRole.Admin)
        {
            return StatusCode(403, new
            {
                code = "FORBIDDEN",
                message = "Doar Admin poate administra utilizatori."
            });
        }

        return null;
    }

    // Verifica echipa si superiorul unui utilizator.
    private async Task<IActionResult?> ValidateHierarchyAsync(
        UserRole role,
        int? teamId,
        int? managerId,
        int? userId = null)
    {
        if (role == UserRole.Manager)
        {
            if (!teamId.HasValue || !managerId.HasValue)
            {
                return BadRequest(new
                {
                    code = "MANAGER_HIERARCHY_REQUIRED",
                    message = "Un Manager trebuie sa aiba o echipa si un superior atribuit."
                });
            }
        }

        if (role == UserRole.Employee && !teamId.HasValue)
        {
            return BadRequest(new
            {
                code = "EMPLOYEE_TEAM_REQUIRED",
                message = "Un angajat trebuie sa apartina unei echipe."
            });
        }

        if (teamId.HasValue)
        {
            var teamExists = await _db.Teams.AnyAsync(t =>
                t.Id == teamId.Value && t.IsActive);

            if (!teamExists)
            {
                return BadRequest(new
                {
                    code = "INVALID_TEAM",
                    message = "Echipa nu exista sau este dezactivata."
                });
            }
        }

        if (managerId.HasValue)
        {
            if (userId.HasValue && managerId.Value == userId.Value)
            {
                return BadRequest(new
                {
                    code = "INVALID_MANAGER",
                    message = "Un utilizator nu poate fi propriul manager."
                });
            }

            var manager = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                    u.Id == managerId.Value && u.IsActive);

            if (manager == null ||
                (manager.Role != UserRole.Manager &&
                 manager.Role != UserRole.Admin))
            {
                return BadRequest(new
                {
                    code = "INVALID_MANAGER",
                    message = "Superiorul trebuie sa fie un Manager sau Admin activ."
                });
            }

            if (role == UserRole.Employee &&
                manager.Role == UserRole.Manager &&
                manager.TeamId != teamId)
            {
                return BadRequest(new
                {
                    code = "MANAGER_TEAM_MISMATCH",
                    message = "Managerul direct al angajatului trebuie sa apartina aceleiasi echipe."
                });
            }
        }

        return null;
    }

    private bool TryGetCurrentUserId(out int userId)
    {
        var userIdText = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return int.TryParse(userIdText, out userId);
    }

    private static bool TryParseRole(
        string? roleText,
        out UserRole role)
    {
        return ApiInput.TryParseEnumName(roleText, out role);
    }

    private static bool IsValidEmail(string email)
    {
        return email.Length <= 450 && System.Net.Mail.MailAddress.TryCreate(
                   email, out var address) &&
               address.Address == email;
    }

    private IActionResult InvalidToken()
    {
        return Unauthorized(new
        {
            code = "INVALID_TOKEN",
            message = "Identificatorul utilizatorului este invalid."
        });
    }

    private IActionResult InactiveUser()
    {
        return Unauthorized(new
        {
            code = "USER_INACTIVE",
            message = "Contul utilizatorului este inactiv."
        });
    }

    private IActionResult InvalidRole()
    {
        return BadRequest(new
        {
            code = "INVALID_ROLE",
            message = "Rolul trebuie sa fie Employee, Manager sau Admin."
        });
    }
}

public class CreateUserRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "Employee";
    public int? TeamId { get; set; }
    public int? ManagerId { get; set; }
}

public class UpdateUserRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Role { get; set; }
    public int? TeamId { get; set; }
    public int? ManagerId { get; set; }
}
