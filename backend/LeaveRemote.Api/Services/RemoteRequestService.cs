using LeaveRemote.Api.Controllers;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using LeaveRemote.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>
/// Zilele de lucru remote. Fiecare zi este o cerere separată (start = end), care refolosește
/// același flux de stări ca și concediile și poate fi aprobată, respinsă sau anulată independent.
/// </summary>
public sealed class RemoteRequestService(
    AppDbContext db,
    SettingsService settings,
    NotificationService notifications,
    AppClock clock)
{
    private static ServiceError NotFound(bool ownedOnly) => ServiceError.NotFound("REMOTE_REQUEST_NOT_FOUND",
        ownedOnly ? "Cererea remote nu există sau nu îți aparține." : "Cererea remote nu există.");

    public async Task<List<OwnRemoteRequestView>> GetOwnAsync(User user)
    {
        var requests = await db.RemoteRequests.AsNoTracking()
            .Include(r => r.Days)
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var today = clock.Today;
        return requests.Select(r => new OwnRemoteRequestView(
            r.Id, r.UserId, r.Status.ToString(), r.Days.Select(d => d.Date).Order().ToList(),
            r.Comment, r.RejectionReason, r.CreatedAt,
            CanEdit: r.Status == RequestStatus.Pending,
            CanCancel: CanCancel(r, today))).ToList();
    }

    /// <summary>
    /// Creează câte o cerere pentru fiecare zi selectată sau, la editare (<paramref name="requestId"/>),
    /// mută cererea în așteptare pe o altă zi. Toate zilele se validează împreună: tot sau nimic.
    /// </summary>
    public async Task<Result<SavedRemoteRequestView>> SaveAsync(User user, RemoteRequestInput input, int? requestId)
    {
        RemoteRequest? existing = null;
        if (requestId.HasValue)
        {
            existing = await db.RemoteRequests.Include(r => r.Days)
                .FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == user.Id);
            if (existing is null) return NotFound(ownedOnly: true);
            if (existing.Status != RequestStatus.Pending)
            {
                return ServiceError.Conflict("INVALID_REQUEST_STATUS", "Doar cererile în așteptare pot fi editate.");
            }
        }

        if (input.Days is not { Count: > 0 })
        {
            return ServiceError.BadRequest("REMOTE_DAYS_REQUIRED", "Selectează cel puțin o zi remote.");
        }

        var days = input.Days.Distinct().Order().ToList();
        if (days.Any(d => d == default))
        {
            return ServiceError.BadRequest("INVALID_REMOTE_DATE", "Una dintre datele selectate este invalidă.");
        }
        if (existing is not null && days.Count != 1)
        {
            return ServiceError.BadRequest("REMOTE_EDIT_SINGLE_DAY",
                "O cerere remote acoperă o singură zi; la editare selectează exact o zi.");
        }

        // La creare se fixează regulile în vigoare; editarea folosește regulile salvate pe cerere.
        RemoteRequest rules;
        if (existing is not null)
        {
            rules = existing;
        }
        else
        {
            var current = await settings.GetRemoteRulesAsync();
            if (current.Error is not null) return current.Error;
            rules = new RemoteRequest
            {
                MinDaysAhead = current.Value!.MinDaysAhead,
                MaxDaysAhead = current.Value.MaxDaysAhead,
                MaxDaysPerWeek = current.Value.MaxDaysPerWeek,
                Status = current.Value.RequiresApproval ? RequestStatus.Pending : RequestStatus.Approved,
                WasAutoApproved = !current.Value.RequiresApproval,
                HolidayDates = await settings.GetHolidayDatesAsync()
            };
        }

        // Cerința 12 și regula 4: orizontul configurat, fără weekend și sărbători legale.
        var today = clock.Today;
        var holidays = rules.HolidayDates.ToHashSet();
        foreach (var day in days)
        {
            var daysAhead = day.DayNumber - today.DayNumber;
            if (daysAhead < rules.MinDaysAhead || daysAhead > rules.MaxDaysAhead)
            {
                return ServiceError.BadRequest("REMOTE_DATE_OUT_OF_RANGE",
                    $"Ziua {day:yyyy-MM-dd} este în afara intervalului permis: între {rules.MinDaysAhead} și {rules.MaxDaysAhead} zile de acum.");
            }
            if (WorkingDays.IsWeekend(day))
            {
                return ServiceError.BadRequest("REMOTE_WEEKEND_FORBIDDEN", $"Ziua {day:yyyy-MM-dd} este în weekend.");
            }
            if (holidays.Contains(day))
            {
                return ServiceError.BadRequest("REMOTE_HOLIDAY_FORBIDDEN", $"Ziua {day:yyyy-MM-dd} este sărbătoare legală.");
            }
        }

        // Regula 1: nicio altă cerere activă (concediu sau remote) în aceeași zi.
        var overlapsLeave = await db.LeaveRequests.AsNoTracking().AnyAsync(r =>
            r.UserId == user.Id &&
            (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved) &&
            days.Any(d => d >= r.StartDate && d <= r.EndDate));
        if (overlapsLeave)
        {
            return ServiceError.Conflict("REMOTE_LEAVE_OVERLAP", "Una dintre zilele selectate se suprapune cu un concediu activ.");
        }

        var overlapsRemote = await db.RemoteRequestDays.AsNoTracking().AnyAsync(d =>
            days.Contains(d.Date) && (!requestId.HasValue || d.RemoteRequestId != requestId.Value) &&
            db.RemoteRequests.Any(r => r.Id == d.RemoteRequestId && r.UserId == user.Id &&
                (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved)));
        if (overlapsRemote)
        {
            return ServiceError.Conflict("REMOTE_REQUEST_OVERLAP", "Una dintre zilele selectate are deja o cerere remote activă.");
        }

        // Regula 5: limita săptămânală (luni–duminică), numărând zilele deja aprobate.
        if (await ValidateWeeklyLimitAsync(user.Id, days, rules.MaxDaysPerWeek, requestId) is { } weeklyError)
        {
            return weeklyError;
        }

        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (existing is not null)
        {
            existing.Comment = comment;
            existing.Days.RemoveAll(d => !days.Contains(d.Date));
            if (existing.Days.Count == 0) existing.Days.Add(new RemoteRequestDay { Date = days[0] });
            await db.SaveChangesAsync();
            return new SavedRemoteRequestView([existing.Id], existing.UserId, existing.Status.ToString(), days, comment);
        }

        var created = days.Select(date => new RemoteRequest
        {
            UserId = user.Id,
            Status = rules.Status,
            WasAutoApproved = rules.WasAutoApproved,
            Comment = comment,
            CreatedAt = clock.UtcNow,
            MinDaysAhead = rules.MinDaysAhead,
            MaxDaysAhead = rules.MaxDaysAhead,
            MaxDaysPerWeek = rules.MaxDaysPerWeek,
            HolidayDates = rules.HolidayDates,
            Days = [new RemoteRequestDay { Date = date }]
        }).ToList();
        db.RemoteRequests.AddRange(created);
        await db.SaveChangesAsync();

        // Un singur email pentru tot formularul: confirmare (aprobare automată) sau anunț către aprobatori.
        await notifications.RemoteSubmittedAsync(days, user, autoApproved: rules.Status == RequestStatus.Approved);

        return new SavedRemoteRequestView(created.Select(r => r.Id).ToList(), user.Id, rules.Status.ToString(), days, comment);
    }

    public async Task<Result<List<PendingRemoteView>>> GetPendingAsync(User reviewer)
    {
        if (!ReviewPolicy.IsReviewer(reviewer)) return ServiceError.Forbidden();

        var requesters = db.Users.AsNoTracking().ReviewableBy(reviewer);
        var pending = await (
            from remote in db.RemoteRequests.AsNoTracking().Include(r => r.Days)
            join requester in requesters on remote.UserId equals requester.Id
            where remote.Status == RequestStatus.Pending
            orderby remote.CreatedAt, remote.Id
            select new { Remote = remote, requester.FirstName, requester.LastName })
            .ToListAsync();

        return pending.Select(x => new PendingRemoteView(
            x.Remote.Id, x.Remote.UserId, x.FirstName, x.LastName, x.Remote.Comment, x.Remote.CreatedAt,
            x.Remote.Days.Select(d => d.Date).Order().ToList())).ToList();
    }

    public async Task<Result<RequestOutcomeView>> ApproveAsync(User reviewer, int id)
    {
        var request = await db.RemoteRequests.Include(r => r.Days).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound(ownedOnly: false);

        var requester = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (ReviewPolicy.Check(reviewer, requester, isApproval: true) is { } denied) return denied;

        if (request.Status != RequestStatus.Pending) return NotPending("aprobate");

        // Limita săptămânală se verifică din nou, față de zilele aprobate între timp.
        if (await ValidateWeeklyLimitAsync(request.UserId, request.Days.Select(d => d.Date),
                request.MaxDaysPerWeek, id) is { } weeklyError)
        {
            return weeklyError;
        }

        request.Status = RequestStatus.Approved;
        await db.SaveChangesAsync();
        notifications.RemoteApproved(request.Days.Select(d => d.Date), requester!, reviewer);
        return new RequestOutcomeView(id, nameof(RequestStatus.Approved), null, "Cererea remote a fost aprobată.");
    }

    public async Task<Result<RequestOutcomeView>> RejectAsync(User reviewer, int id, string? reason)
    {
        var request = await db.RemoteRequests.Include(r => r.Days).FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound(ownedOnly: false);

        var requester = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (ReviewPolicy.Check(reviewer, requester, isApproval: false) is { } denied) return denied;

        if (request.Status != RequestStatus.Pending) return NotPending("respinse");

        var trimmed = reason?.Trim();
        if (!ApiInput.HasValidRejectionReason(trimmed))
        {
            return ServiceError.BadRequest("INVALID_REJECTION_REASON", "Motivul respingerii trebuie să conțină minimum 10 caractere.");
        }

        request.Status = RequestStatus.Rejected;
        request.RejectionReason = trimmed;
        await db.SaveChangesAsync();
        notifications.RemoteRejected(request.Days.Select(d => d.Date), requester!, reviewer, trimmed!);
        return new RequestOutcomeView(id, nameof(RequestStatus.Rejected), trimmed, "Cererea remote a fost respinsă.");
    }

    public async Task<Result<RequestOutcomeView>> CancelAsync(User user, int id)
    {
        var request = await db.RemoteRequests.Include(r => r.Days)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == user.Id);
        if (request is null) return NotFound(ownedOnly: true);

        if (request.Status is not (RequestStatus.Pending or RequestStatus.Approved))
        {
            return ServiceError.Conflict("INVALID_REQUEST_STATUS", "Cererea nu mai poate fi anulată.");
        }
        if (!CanCancel(request, clock.Today))
        {
            return ServiceError.Conflict("REMOTE_ALREADY_STARTED", "O zi remote aprobată poate fi anulată doar înainte de data ei.");
        }

        var wasApproved = request.Status == RequestStatus.Approved;
        request.Status = RequestStatus.Canceled;
        await db.SaveChangesAsync();
        await notifications.RemoteCancelledAsync(request.Days.Select(d => d.Date), user, wasApproved);
        return new RequestOutcomeView(id, nameof(RequestStatus.Canceled), null, "Cererea remote a fost anulată.");
    }

    public static bool CanCancel(RemoteRequest request, DateOnly today) =>
        request.Status == RequestStatus.Pending ||
        (request.Status == RequestStatus.Approved && request.Days.All(day => day.Date > today));

    private async Task<ServiceError?> ValidateWeeklyLimitAsync(
        int userId, IEnumerable<DateOnly> days, int limit, int? excludeRequestId)
    {
        foreach (var week in days.GroupBy(WorkingDays.MondayOf))
        {
            var monday = week.Key;
            var sunday = DateOnly.FromDayNumber(Math.Min(monday.DayNumber + 6, DateOnly.MaxValue.DayNumber));

            var approvedDays = await db.RemoteRequestDays.AsNoTracking()
                .Where(d => d.Date >= monday && d.Date <= sunday &&
                    (!excludeRequestId.HasValue || d.RemoteRequestId != excludeRequestId.Value) &&
                    db.RemoteRequests.Any(r => r.Id == d.RemoteRequestId && r.UserId == userId &&
                        r.Status == RequestStatus.Approved))
                .Select(d => d.Date)
                .Distinct()
                .CountAsync();

            if (approvedDays + week.Count() > limit)
            {
                return ServiceError.Conflict("REMOTE_WEEKLY_LIMIT_EXCEEDED",
                    $"Limita de {limit} zile remote pe săptămână ar fi depășită pentru săptămâna care începe la {monday:yyyy-MM-dd}.");
            }
        }
        return null;
    }

    private static ServiceError NotPending(string action) =>
        ServiceError.Conflict("INVALID_REQUEST_STATUS", $"Doar cererile în așteptare pot fi {action}.");
}
