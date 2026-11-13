using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

[Route("api/remote-requests")]
[Authorize]
public class RemoteRequestsController(RemoteRequestService remote) : ApiControllerBase
{
    /// <summary>Zilele remote ale utilizatorului curent (o cerere per zi).</summary>
    [HttpGet]
    public Task<IActionResult> GetOwn() =>
        AsCurrentUser(async user => Ok(await remote.GetOwnAsync(user)));

    /// <summary>Una sau mai multe zile remote dintr-un singur formular.</summary>
    [HttpPost]
    [RequestWriteTransaction]
    public Task<IActionResult> Create([FromBody] RemoteRequestInput input) =>
        AsCurrentUser(async user => Respond(await remote.SaveAsync(user, input, null),
            saved => Created("/api/remote-requests", saved)));

    [HttpPut("{id:int}")]
    [RequestWriteTransaction]
    public Task<IActionResult> Edit(int id, [FromBody] RemoteRequestInput input) =>
        AsCurrentUser(async user => Respond(await remote.SaveAsync(user, input, id)));

    [HttpGet("pending")]
    [Authorize(Roles = "Manager,Admin")]
    public Task<IActionResult> GetPending() =>
        AsCurrentUser(async user => Respond(await remote.GetPendingAsync(user)));

    [HttpPatch("{id:int}/approve")]
    [Authorize(Roles = "Manager,Admin")]
    [RequestWriteTransaction]
    public Task<IActionResult> Approve(int id) =>
        AsCurrentUser(async user => Respond(await remote.ApproveAsync(user, id)));

    [HttpPatch("{id:int}/reject")]
    [Authorize(Roles = "Manager,Admin")]
    [RequestWriteTransaction]
    public Task<IActionResult> Reject(int id, [FromBody] RejectRequestInput input) =>
        AsCurrentUser(async user => Respond(await remote.RejectAsync(user, id, input.Reason)));

    [HttpPatch("{id:int}/cancel")]
    [RequestWriteTransaction]
    public Task<IActionResult> Cancel(int id) =>
        AsCurrentUser(async user => Respond(await remote.CancelAsync(user, id)));
}
