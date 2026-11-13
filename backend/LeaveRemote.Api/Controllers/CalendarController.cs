using System.Security.Claims;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/calendar")]
public class CalendarController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMonth(
        [FromQuery] int? year, [FromQuery] int? month, [FromQuery] int? teamId, [FromQuery] string? type)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return ApiErrors.Unauthenticated();
        }
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user == null) return ApiErrors.Unauthenticated();

        var today = DateTime.Today;
        var selectedYear = year ?? today.Year;
        var selectedMonth = month ?? today.Month;
        if (selectedYear is < 1 or > 9999 || selectedMonth is < 1 or > 12)
        {
            return BadRequest(new { code = "INVALID_CALENDAR_MONTH", message = "Anul sau luna este invalidă." });
        }

        var start = new DateOnly(selectedYear, selectedMonth, 1);
        var end = new DateOnly(selectedYear, selectedMonth, DateTime.DaysInMonth(selectedYear, selectedMonth));
        var remoteOnly = string.Equals(type?.Trim(), "Remote", StringComparison.OrdinalIgnoreCase);
        LeaveType? leaveType = null;
        if (!string.IsNullOrWhiteSpace(type) && !remoteOnly)
        {
            if (!ApiInput.TryParseEnumName<LeaveType>(type, out var parsedType))
            {
                return BadRequest(new { code = "INVALID_CALENDAR_TYPE", message = "Tipul de absență este invalid." });
            }
            leaveType = parsedType;
        }

        if (user.Role != UserRole.Admin)
        {
            if (!user.TeamId.HasValue || (teamId.HasValue && teamId != user.TeamId)) return ApiErrors.Forbidden();
            teamId = user.TeamId;
        }
        else if (teamId.HasValue && !await db.Teams.AnyAsync(t => t.Id == teamId.Value))
        {
            return NotFound(new { code = "TEAM_NOT_FOUND", message = "Echipa nu există." });
        }

        var membersQuery = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (teamId.HasValue) membersQuery = membersQuery.Where(u => u.TeamId == teamId.Value);

        var members = await membersQuery
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.TeamId }).ToListAsync();
        var memberIds = membersQuery.Select(u => u.Id);
        var events = new List<CalendarEvent>();

        if (!remoteOnly)
        {
            var leave = await db.LeaveRequests.AsNoTracking()
                .Where(r => memberIds.Contains(r.UserId) && r.StartDate <= end && r.EndDate >= start &&
                    (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved) &&
                    (!leaveType.HasValue || r.Type == leaveType.Value))
                .Select(r => new { r.Id, r.UserId, r.Type, r.Status, r.WorkingDates })
                .ToListAsync();

            events.AddRange(leave.SelectMany(r => r.WorkingDates.Where(d => d >= start && d <= end)
                .Select(d => new CalendarEvent(r.Id, r.UserId, d, r.Type.ToString(), r.Status.ToString()))));
        }

        if (!leaveType.HasValue)
        {
            var remote = await (
                from day in db.RemoteRequestDays.AsNoTracking()
                join request in db.RemoteRequests.AsNoTracking() on day.RemoteRequestId equals request.Id
                where memberIds.Contains(request.UserId) && day.Date >= start && day.Date <= end &&
                      (request.Status == RequestStatus.Pending || request.Status == RequestStatus.Approved)
                select new { request.Id, request.UserId, day.Date, request.Status })
                .ToListAsync();

            events.AddRange(remote.Select(r => new CalendarEvent(r.Id, r.UserId, r.Date, "Remote", r.Status.ToString())));
        }

        var holidays = await db.PublicHolidays.AsNoTracking()
            .Where(holiday => holiday.Date >= start && holiday.Date <= end)
            .OrderBy(holiday => holiday.Date)
            .Select(holiday => new { holiday.Date, holiday.Name })
            .ToListAsync();

        return Ok(new
        {
            Year = selectedYear,
            Month = selectedMonth,
            TeamId = teamId,
            Members = members,
            Holidays = holidays,
            Events = events.OrderBy(e => e.Date).ThenBy(e => e.UserId).ThenBy(e => e.RequestId)
        });
    }

    private record CalendarEvent(int RequestId, int UserId, DateOnly Date, string Type, string Status);
}
