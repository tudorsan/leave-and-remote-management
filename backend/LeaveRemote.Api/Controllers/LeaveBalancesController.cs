using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

[Route("api/leave-balances")]
[Authorize]
public class LeaveBalancesController(LeaveBalanceService balances) : ApiControllerBase
{
    [HttpGet("me")]
    public Task<IActionResult> GetOwn([FromQuery] int? year) =>
        AsCurrentUser(async user => Respond(await balances.GetBalanceAsync(user, null, year)));

    /// <summary>Soldul altui utilizator: Managerul pentru echipa sa și subordonații direcți, Adminul pentru toți.</summary>
    [HttpGet("{userId:int}")]
    public Task<IActionResult> Get(int userId, [FromQuery] int? year) =>
        AsCurrentUser(async user => Respond(await balances.GetBalanceAsync(user, userId, year)));

    /// <summary>Admin setează maximul anual de zile de concediu de odihnă al unui angajat.</summary>
    [HttpPut("{userId:int}/{year:int}")]
    [Authorize(Roles = "Admin")]
    [RequestWriteTransaction]
    public async Task<IActionResult> SetAllocation(int userId, int year, [FromBody] SetAllocationInput input) =>
        Respond(await balances.SetAllocationAsync(userId, year, input.TotalDays));
}
