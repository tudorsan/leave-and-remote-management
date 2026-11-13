using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

/// <summary>
/// Cerința 6: exportul propriilor zile aprobate. Fiecare utilizator exportă doar propriile cereri aprobate;
/// regula este aplicată de backend, nu de interfață.
/// </summary>
[Route("api/exports")]
[Authorize]
public class ExportsController(ExportService exports) : ApiControllerBase
{
    /// <summary>Fișier iCalendar, de importat în Google Calendar, Outlook sau calendarul telefonului.</summary>
    [HttpGet("my-approved-days.ics")]
    public Task<IActionResult> ICalendar() =>
        AsCurrentUser(async user => Download(await exports.ToICalendarAsync(user)));

    /// <summary>Tabel CSV, de deschis în Excel.</summary>
    [HttpGet("my-approved-days.csv")]
    public Task<IActionResult> Csv() =>
        AsCurrentUser(async user => Download(await exports.ToCsvAsync(user)));

    private FileContentResult Download(ExportFile file) => File(file.Content, file.ContentType, file.FileName);
}
