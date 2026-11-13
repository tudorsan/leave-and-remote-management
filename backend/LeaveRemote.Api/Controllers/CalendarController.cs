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
}
