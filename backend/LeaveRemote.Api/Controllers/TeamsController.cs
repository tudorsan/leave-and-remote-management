using System.Security.Claims;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Controllers;

[ApiController]
[Route("api/teams")]
[Authorize(Roles = "Admin")]
public class TeamsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TeamsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetTeams()
    {
        var teams = await _db.Teams
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.IsActive
            })
            .ToListAsync();

        return Ok(teams);
    }

    [HttpPost]
    [RequestWriteTransaction]
    public async Task<IActionResult> CreateTeam([FromBody] CreateTeamRequest request)
    {
        if (!await IsCurrentAdminAsync()) return Forbid();

        var name = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new
            {
                code = "INVALID_TEAM_NAME",
                message = "Numele echipei este obligatoriu."
            });
        }

        var exists = await _db.Teams.AnyAsync(t => t.Name == name);

        if (exists)
        {
            return Conflict(new
            {
                code = "TEAM_ALREADY_EXISTS",
                message = "Există deja o echipă cu acest nume."
            });
        }

        var team = new LeaveRemote.Api.Models.Team
        {
            Name = name,
            IsActive = true
        };

        _db.Teams.Add(team);
        await _db.SaveChangesAsync();

        return Created($"/api/teams/{team.Id}", new
        {
            team.Id,
            team.Name,
            team.IsActive
        });
    }

    [HttpPut("{id:int}")]
    [RequestWriteTransaction]
        public async Task<IActionResult> UpdateTeam(
            int id,
            [FromBody] CreateTeamRequest request)
        {
            if (!await IsCurrentAdminAsync()) return Forbid();

            var name = request.Name?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest(new
                {
                    code = "INVALID_TEAM_NAME",
                    message = "Numele echipei este obligatoriu."
                });
            }

            var team = await _db.Teams.FindAsync(id);

            if (team == null)
            {
                return NotFound(new
                {
                    code = "TEAM_NOT_FOUND",
                    message = "Echipa nu a fost găsită."
                });
            }

            var exists = await _db.Teams
                .AnyAsync(t => t.Id != id && t.Name == name);

            if (exists)
            {
                return Conflict(new
                {
                    code = "TEAM_ALREADY_EXISTS",
                    message = "Există deja o echipă cu acest nume."
                });
            }

            team.Name = name;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                team.Id,
                team.Name,
                team.IsActive
            });
    }

    [HttpPatch("{id:int}/deactivate")]
    [RequestWriteTransaction]
    public async Task<IActionResult> DeactivateTeam(int id)
    {
        if (!await IsCurrentAdminAsync()) return Forbid();

        var team = await _db.Teams.FindAsync(id);

        if (team == null)
        {
            return NotFound(new
            {
                code = "TEAM_NOT_FOUND",
                message = "Echipa nu a fost găsită."
            });
        }

        if (!team.IsActive)
        {
            return Conflict(new
            {
                code = "TEAM_ALREADY_INACTIVE",
                message = "Echipa este deja dezactivată."
            });
        }

        if (await _db.Users.AnyAsync(u => u.TeamId == id && u.IsActive))
        {
            return Conflict(new { code = "TEAM_HAS_ACTIVE_MEMBERS", message = "Mută membrii activi înainte de dezactivarea echipei." });
        }

        team.IsActive = false;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            team.Id,
            team.Name,
            team.IsActive
        });
    }

    private Task<bool> IsCurrentAdminAsync() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? _db.Users.AnyAsync(user => user.Id == userId && user.IsActive && user.Role == UserRole.Admin)
            : Task.FromResult(false);
}

public class CreateTeamRequest
{
    public string Name { get; set; } = string.Empty;
}
