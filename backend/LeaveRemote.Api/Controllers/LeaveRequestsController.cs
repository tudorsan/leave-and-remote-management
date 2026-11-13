using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

[Route("api/leave-requests")]
[Authorize]
public class LeaveRequestsController(LeaveRequestService leaves) : ApiControllerBase
{
    /// <summary>Cererile de concediu ale utilizatorului curent.</summary>
    [HttpGet]
    public Task<IActionResult> GetOwn() =>
        AsCurrentUser(async user => Ok(await leaves.GetOwnAsync(user)));

    [HttpPost]
    [RequestWriteTransaction]
    public Task<IActionResult> Create([FromBody] LeaveRequestInput input) =>
        AsCurrentUser(async user => Respond(await leaves.SaveAsync(user, input, null),
            saved => Created($"/api/leave-requests/{saved.Id}", saved)));

    [HttpPut("{id:int}")]
    [RequestWriteTransaction]
    public Task<IActionResult> Edit(int id, [FromBody] LeaveRequestInput input) =>
        AsCurrentUser(async user => Respond(await leaves.SaveAsync(user, input, id)));

    /// <summary>Cererile în așteptare pe care utilizatorul curent le poate aproba.</summary>
    [HttpGet("pending")]
    [Authorize(Roles = "Manager,Admin")]
    public Task<IActionResult> GetPending() =>
        AsCurrentUser(async user => Respond(await leaves.GetPendingAsync(user)));

    [HttpPatch("{id:int}/approve")]
    [Authorize(Roles = "Manager,Admin")]
    [RequestWriteTransaction]
    public Task<IActionResult> Approve(int id) =>
        AsCurrentUser(async user => Respond(await leaves.ApproveAsync(user, id)));

    [HttpPatch("{id:int}/reject")]
    [Authorize(Roles = "Manager,Admin")]
    [RequestWriteTransaction]
    public Task<IActionResult> Reject(int id, [FromBody] RejectRequestInput input) =>
        AsCurrentUser(async user => Respond(await leaves.RejectAsync(user, id, input.Reason)));

    [HttpPatch("{id:int}/cancel")]
    [RequestWriteTransaction]
    public Task<IActionResult> Cancel(int id) =>
        AsCurrentUser(async user => Respond(await leaves.CancelAsync(user, id)));
}
