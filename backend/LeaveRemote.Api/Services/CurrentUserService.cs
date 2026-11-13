using System.Security.Claims;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>Încarcă din baza de date utilizatorul activ care face cererea curentă.</summary>
public sealed class CurrentUserService(AppDbContext db)
{
    public async Task<Result<User>> GetAsync(ClaimsPrincipal principal)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return ServiceError.Unauthorized("INVALID_TOKEN", "Identificatorul utilizatorului este invalid.");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        return user is null
            ? ServiceError.Unauthorized("USER_INACTIVE", "Contul utilizatorului este inactiv.")
            : user;
    }
}
