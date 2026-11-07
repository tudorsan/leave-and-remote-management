using LeaveRemote.Api.Data;
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

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var query = _db.Users.AsNoTracking();

        if (User.IsInRole("Manager"))
        {
            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdText, out var managerId))
            {
                return Unauthorized(new
                {
                    code = "INVALID_TOKEN",
                    message = "Identificatorul utilizatorului este invalid."
                });
            }

            var manager = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == managerId && u.IsActive);

            if (manager?.TeamId == null)
            {
                return Forbid();
            }

            query = query.Where(u => u.TeamId == manager.TeamId);
        }

        var users = await query
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                Role = u.Role.ToString(),
                u.TeamId,
                u.IsActive
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
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
                message = "Numele, prenumele, emailul și parola sunt obligatorii."
            });
        }

        if (!System.Net.Mail.MailAddress.TryCreate(email, out var address) ||
            address.Address != email)
        {
            return BadRequest(new
            {
                code = "INVALID_EMAIL",
                message = "Adresa de email este invalidă."
            });
        }

        if (request.Password.Length < 8)
        {
            return BadRequest(new
            {
                code = "INVALID_PASSWORD",
                message = "Parola trebuie să conțină minimum 8 caractere."
            });
        }

        if (!Enum.TryParse<LeaveRemote.Api.Models.UserRole>(
                request.Role, true, out var role) ||
            !Enum.IsDefined(role))
        {
            return BadRequest(new
            {
                code = "INVALID_ROLE",
                message = "Rolul trebuie să fie Employee, Manager sau Admin."
            });
        }

        var emailExists = await _db.Users
            .AnyAsync(u => u.Email.ToLower() == email);

        if (emailExists)
        {
            return Conflict(new
            {
                code = "EMAIL_ALREADY_EXISTS",
                message = "Există deja un utilizator cu această adresă de email."
            });
        }

        if (request.TeamId.HasValue)
        {
            var teamExists = await _db.Teams
                .AnyAsync(t => t.Id == request.TeamId.Value && t.IsActive);

            if (!teamExists)
            {
                return BadRequest(new
                {
                    code = "INVALID_TEAM",
                    message = "Echipa nu există sau este dezactivată."
                });
            }
        }

        if (request.ManagerId.HasValue)
        {
            var managerExists = await _db.Users
                .AnyAsync(u => u.Id == request.ManagerId.Value &&
                            u.IsActive &&
                            u.Role == LeaveRemote.Api.Models.UserRole.Manager);

            if (!managerExists)
            {
                return BadRequest(new
                {
                    code = "INVALID_MANAGER",
                    message = "Managerul nu există, este inactiv sau nu are rolul Manager."
                });
            }
        }

        var user = new LeaveRemote.Api.Models.User
        {
            FirstName = firstName!,
            LastName = lastName!,
            Email = email!,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            IsActive = true,
            TeamId = request.TeamId,
            ManagerId = request.ManagerId
        };

        _db.Users.Add(user);
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

        [HttpPatch("{id:int}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeactivateUser(int id)
    {
        var user = await _db.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound(new
            {
                code = "USER_NOT_FOUND",
                message = "Utilizatorul nu a fost găsit."
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

        var currentUserIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (int.TryParse(currentUserIdText, out var currentUserId) &&
            currentUserId == id)
        {
            return BadRequest(new
            {
                code = "CANNOT_DEACTIVATE_SELF",
                message = "Nu îți poți dezactiva propriul cont."
            });
        }

        user.IsActive = false;
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