using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

[Route("api/calendar")]
[Authorize]
public class CalendarController(CalendarService calendar) : ApiControllerBase
{
    /// <summary>Calendarul lunar al echipei; filtrul de echipă este disponibil doar pentru Admin.</summary>
    [HttpGet]
    public Task<IActionResult> GetMonth(
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] int? teamId, [FromQuery] string? type) =>
        AsCurrentUser(async user => Respond(await calendar.GetMonthAsync(user, year, month, teamId, type)));

    /// <summary>
    /// Calendarul pe un interval de zile, folosit de vederea săptămânală (cerința 6).
    /// Fără parametri: săptămâna curentă (luni–duminică).
    /// </summary>
    [HttpGet("range")]
    public Task<IActionResult> GetRange(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? teamId, [FromQuery] string? type) =>
        AsCurrentUser(async user => Respond(await calendar.GetRangeAsync(user, from, to, teamId, type)));
}
