using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>Administrarea echipelor (Admin). Echipele se dezactivează, nu se șterg.</summary>
public sealed class TeamService(AppDbContext db)
{
    private static readonly ServiceError NotFound = ServiceError.NotFound("TEAM_NOT_FOUND", "Echipa nu a fost găsită.");

    public Task<List<TeamView>> GetAllAsync() =>
        db.Teams.AsNoTracking().OrderBy(t => t.Name).Select(t => new TeamView(t.Id, t.Name, t.IsActive)).ToListAsync();

    public async Task<Result<TeamView>> CreateAsync(TeamInput input)
    {
        var name = input.Name?.Trim();
        if (await ValidateNameAsync(name, null) is { } error) return error;

        var team = new Team { Name = name!, IsActive = true };
        db.Teams.Add(team);
        await db.SaveChangesAsync();
        return ToView(team);
    }

    public async Task<Result<TeamView>> RenameAsync(int id, TeamInput input)
    {
        var name = input.Name?.Trim();
        var team = await db.Teams.FindAsync(id);
        if (team is null) return NotFound;
        if (await ValidateNameAsync(name, id) is { } error) return error;

        team.Name = name!;
        await db.SaveChangesAsync();
        return ToView(team);
    }

    public async Task<Result<TeamView>> DeactivateAsync(int id)
    {
        var team = await db.Teams.FindAsync(id);
        if (team is null) return NotFound;
        if (!team.IsActive) return ServiceError.Conflict("TEAM_ALREADY_INACTIVE", "Echipa este deja dezactivată.");
        if (await db.Users.AnyAsync(u => u.TeamId == id && u.IsActive))
        {
            return ServiceError.Conflict("TEAM_HAS_ACTIVE_MEMBERS", "Mută membrii activi înainte de dezactivarea echipei.");
        }

        team.IsActive = false;
        await db.SaveChangesAsync();
        return ToView(team);
    }

    private async Task<ServiceError?> ValidateNameAsync(string? name, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ServiceError.BadRequest("INVALID_TEAM_NAME", "Numele echipei este obligatoriu.");
        }
        return await db.Teams.AnyAsync(t => t.Name == name && (!excludeId.HasValue || t.Id != excludeId.Value))
            ? ServiceError.Conflict("TEAM_ALREADY_EXISTS", "Există deja o echipă cu acest nume.")
            : null;
    }

    private static TeamView ToView(Team team) => new(team.Id, team.Name, team.IsActive);
}
