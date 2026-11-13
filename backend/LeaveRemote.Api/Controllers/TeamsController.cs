using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

/// <summary>Administrarea echipelor (doar Admin).</summary>
[Route("api/teams")]
[Authorize(Roles = "Admin")]
public class TeamsController(TeamService teams) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await teams.GetAllAsync());

    [HttpPost]
    [RequestWriteTransaction]
    public async Task<IActionResult> Create([FromBody] TeamInput input) =>
        Respond(await teams.CreateAsync(input), team => Created($"/api/teams/{team.Id}", team));

    [HttpPut("{id:int}")]
    [RequestWriteTransaction]
    public async Task<IActionResult> Rename(int id, [FromBody] TeamInput input) =>
        Respond(await teams.RenameAsync(id, input));

    [HttpPatch("{id:int}/deactivate")]
    [RequestWriteTransaction]
    public async Task<IActionResult> Deactivate(int id) =>
        Respond(await teams.DeactivateAsync(id));
}
