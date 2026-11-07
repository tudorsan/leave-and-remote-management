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
}