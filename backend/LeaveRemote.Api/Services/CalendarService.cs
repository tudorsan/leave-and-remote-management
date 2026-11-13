using LeaveRemote.Api.Controllers;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>
/// Calendarul echipei: membrii și, pentru fiecare zi, cine e în concediu sau remote
/// (doar cererile în așteptare și aprobate). Angajatul și Managerul văd doar echipa lor;
/// Adminul vede toate echipele și poate filtra pe echipă.
/// Vederea lunară (cerința 4) și cea săptămânală (cerința 6) folosesc aceeași logică, pe intervale diferite.
/// </summary>
public sealed class CalendarService(AppDbContext db, AppClock clock)
{
    public const string RemoteType = "Remote";

    /// <summary>Cel mai lung interval acceptat de vederea pe interval (o săptămână are 7 zile).</summary>
    public const int MaxRangeDays = 42;

    public async Task<Result<CalendarMonthView>> GetMonthAsync(User viewer, int? year, int? month, int? teamId, string? type)
    {
        var today = clock.Today;
        var selectedYear = year ?? today.Year;
        var selectedMonth = month ?? today.Month;
        if (selectedYear is < 1 or > 9999 || selectedMonth is < 1 or > 12)
        {
            return ServiceError.BadRequest("INVALID_CALENDAR_MONTH", "Anul sau luna este invalidă.");
        }

        var start = new DateOnly(selectedYear, selectedMonth, 1);
        var end = start.AddDays(DateTime.DaysInMonth(selectedYear, selectedMonth) - 1);
        var data = await LoadAsync(viewer, start, end, teamId, type);
        if (data.Error is not null) return data.Error;

        var (resolvedTeamId, members, holidays, events) = data.Value!;
        return new CalendarMonthView(selectedYear, selectedMonth, resolvedTeamId, members, holidays, events);
    }

    /// <summary>
    /// Calendarul pe un interval oarecare de zile (ex. o săptămână care trece dintr-o lună în alta).
    /// Fără interval: săptămâna curentă, de luni până duminică.
    /// </summary>
    public async Task<Result<CalendarRangeView>> GetRangeAsync(User viewer, DateOnly? from, DateOnly? to, int? teamId, string? type)
    {
        var start = from ?? WorkingDays.MondayOf(clock.Today);
        var end = to ?? start.AddDays(6);
        if (start == default || end == default || end < start || end.DayNumber - start.DayNumber + 1 > MaxRangeDays)
        {
            return ServiceError.BadRequest("INVALID_CALENDAR_RANGE",
                $"Intervalul calendarului este invalid: sfârșitul trebuie să fie după început, iar intervalul de maximum {MaxRangeDays} de zile.");
        }

        var data = await LoadAsync(viewer, start, end, teamId, type);
        if (data.Error is not null) return data.Error;

        var (resolvedTeamId, members, holidays, events) = data.Value!;
        return new CalendarRangeView(start, end, resolvedTeamId, members, holidays, events);
    }

    private sealed record CalendarData(
        int? TeamId, List<CalendarMemberView> Members, List<HolidayView> Holidays, List<CalendarEventView> Events);

    private async Task<Result<CalendarData>> LoadAsync(User viewer, DateOnly start, DateOnly end, int? teamId, string? type)
    {
        var remoteOnly = string.Equals(type?.Trim(), RemoteType, StringComparison.OrdinalIgnoreCase);
        LeaveType? leaveType = null;
        if (!string.IsNullOrWhiteSpace(type) && !remoteOnly)
        {
            if (!ApiInput.TryParseEnumName<LeaveType>(type, out var parsed))
            {
                return ServiceError.BadRequest("INVALID_CALENDAR_TYPE", "Tipul de absență este invalid.");
            }
            leaveType = parsed;
        }

        // Matricea de permisiuni: doar Adminul vede (și filtrează) alte echipe.
        if (viewer.Role != UserRole.Admin)
        {
            if (!viewer.TeamId.HasValue || (teamId.HasValue && teamId != viewer.TeamId)) return ServiceError.Forbidden();
            teamId = viewer.TeamId;
        }
        else if (teamId.HasValue && !await db.Teams.AnyAsync(t => t.Id == teamId.Value))
        {
            return ServiceError.NotFound("TEAM_NOT_FOUND", "Echipa nu există.");
        }

        var membersQuery = db.Users.AsNoTracking().Where(u => u.IsActive);
        if (teamId.HasValue) membersQuery = membersQuery.Where(u => u.TeamId == teamId.Value);

        var members = await membersQuery
            .OrderByDescending(u => u.Role).ThenBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id)
            .Select(u => new CalendarMemberView(u.Id, u.FirstName, u.LastName, u.TeamId))
            .ToListAsync();
        var memberIds = membersQuery.Select(u => u.Id);
        var events = new List<CalendarEventView>();

        if (!remoteOnly)
        {
            var leave = await db.LeaveRequests.AsNoTracking()
                .Where(r => memberIds.Contains(r.UserId) && r.StartDate <= end && r.EndDate >= start &&
                    (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved) &&
                    (!leaveType.HasValue || r.Type == leaveType.Value))
                .Select(r => new { r.Id, r.UserId, r.Type, r.Status, r.WorkingDates })
                .ToListAsync();

            events.AddRange(leave.SelectMany(r => r.WorkingDates
                .Where(d => d >= start && d <= end)
                .Select(d => new CalendarEventView(r.Id, r.UserId, d, r.Type.ToString(), r.Status.ToString()))));
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

            events.AddRange(remote.Select(r => new CalendarEventView(r.Id, r.UserId, r.Date, RemoteType, r.Status.ToString())));
        }

        var holidays = await db.PublicHolidays.AsNoTracking()
            .Where(h => h.Date >= start && h.Date <= end)
            .OrderBy(h => h.Date)
            .Select(h => new HolidayView(h.Date, h.Name))
            .ToListAsync();

        return new CalendarData(teamId, members, holidays,
            events.OrderBy(e => e.Date).ThenBy(e => e.UserId).ThenBy(e => e.RequestId).ToList());
    }
}
