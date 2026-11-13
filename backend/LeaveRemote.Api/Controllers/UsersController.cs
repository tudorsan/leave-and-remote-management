using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

[Route("api/users")]
[Authorize(Roles = "Manager,Admin")]
public class UsersController(UserService users) : ApiControllerBase
{
    /// <summary>Adminul vede toți utilizatorii; Managerul, echipa sa și subordonații direcți.</summary>
    [HttpGet]
    public Task<IActionResult> GetVisible() =>
        AsCurrentUser(async user => Respond(await users.GetVisibleAsync(user)));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [RequestWriteTransaction]
    public async Task<IActionResult> Create([FromBody] CreateUserInput input) =>
        Respond(await users.CreateAsync(input), user => Created($"/api/users/{user.Id}", user));

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    [RequestWriteTransaction]
    public Task<IActionResult> Update(int id, [FromBody] UpdateUserInput input) =>
        AsCurrentUser(async admin => Respond(await users.UpdateAsync(admin, id, input)));

    [HttpPatch("{id:int}/deactivate")]
    [Authorize(Roles = "Admin")]
    [RequestWriteTransaction]
    public Task<IActionResult> Deactivate(int id) =>
        AsCurrentUser(async admin => Respond(await users.DeactivateAsync(admin, id)));

    [HttpPatch("{id:int}/reactivate")]
    [Authorize(Roles = "Admin")]
    [RequestWriteTransaction]
    public async Task<IActionResult> Reactivate(int id) =>
        Respond(await users.ReactivateAsync(id));
}
