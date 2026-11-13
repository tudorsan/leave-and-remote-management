using LeaveRemote.Api.Controllers;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using LeaveRemote.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>Cererile de concediu: creare, editare, aprobare, respingere și anulare.</summary>
public sealed class LeaveRequestService(
    AppDbContext db,
    LeaveBalanceService balances,
    SettingsService settings,
    NotificationService notifications,
    AppClock clock)
{
    private static ServiceError NotFound(bool ownedOnly) => ServiceError.NotFound("LEAVE_REQUEST_NOT_FOUND",
        ownedOnly ? "Cererea de concediu nu există sau nu îți aparține." : "Cererea de concediu nu există.");

    public async Task<List<OwnLeaveRequestView>> GetOwnAsync(User user)
    {
        var requests = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var today = clock.Today;
        return requests.Select(r => new OwnLeaveRequestView(
            r.Id, r.UserId, r.StartDate, r.EndDate, r.Type.ToString(), r.Status.ToString(), r.WorkingDates.Count,
            r.Comment, r.RejectionReason, r.CreatedAt, CanCancel(r, today))).ToList();
    }

    /// <summary>
    /// Creează (<paramref name="requestId"/> null) sau editează o cerere în așteptare.
    /// La creare se salvează setările și sărbătorile în vigoare; editarea folosește acele valori,
    /// deoarece modificarea setărilor nu revalidează cererile existente.
    /// </summary>
    public async Task<Result<SavedLeaveRequestView>> SaveAsync(User user, LeaveRequestInput input, int? requestId)
    {
        LeaveRequest? existing = null;
        if (requestId.HasValue)
        {
            existing = await db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == user.Id);
            if (existing is null) return NotFound(ownedOnly: true);
            if (existing.Status != RequestStatus.Pending)
            {
                return ServiceError.Conflict("INVALID_REQUEST_STATUS", "Doar cererile în așteptare pot fi editate.");
            }
        }

        // Regula 2: end >= start.
        if (input.StartDate == default || input.EndDate == default ||
            input.StartDate > input.EndDate || input.EndDate == DateOnly.MaxValue)
        {
            return ServiceError.BadRequest("INVALID_DATE_RANGE", "Intervalul de concediu este invalid.");
        }

        if (!ApiInput.TryParseEnumName<LeaveType>(input.Type, out var leaveType))
        {
            return ServiceError.BadRequest("INVALID_LEAVE_TYPE", "Tipul concediului este invalid.");
        }

        var request = existing ?? new LeaveRequest { UserId = user.Id, CreatedAt = clock.UtcNow };
        if (existing is null)
        {
            var rules = await settings.GetLeaveRulesAsync();
            if (rules.Error is not null) return rules.Error;
            request.MinDaysAhead = rules.Value!.MinDaysAhead;
            request.MaxDaysAhead = rules.Value.MaxDaysAhead;
            request.TeamAbsenceLimitPercent = rules.Value.MaxTeamAbsentPct;
            request.HolidayDates = await settings.GetHolidayDatesAsync();
        }

        // Regula 2 + setări: nu în trecut; preavizul minim se aplică doar concediului de odihnă.
        var today = clock.Today;
        var minDaysAhead = leaveType == LeaveType.Vacation ? request.MinDaysAhead : 0;
        if (input.StartDate.DayNumber - today.DayNumber < minDaysAhead ||
            input.EndDate.DayNumber - today.DayNumber > request.MaxDaysAhead)
        {
            return ServiceError.BadRequest("LEAVE_DATE_OUT_OF_RANGE",
                $"Concediul trebuie să înceapă cu minimum {minDaysAhead} zile în avans și să se termine în maximum {request.MaxDaysAhead} zile de acum.");
        }

        // Cerința 8: zilele lucrătoare exclud weekendurile și sărbătorile legale.
        var workingDates = WorkingDays.Between(input.StartDate, input.EndDate, request.HolidayDates);
        if (workingDates.Count == 0)
        {
            return ServiceError.BadRequest("NO_WORKING_DAYS", "Intervalul nu conține zile lucrătoare.");
        }

        // Regula 1: o zi nu poate avea două cereri active, indiferent de tip.
        var overlapsLeave = await db.LeaveRequests.AnyAsync(r =>
            r.UserId == user.Id && (!requestId.HasValue || r.Id != requestId.Value) &&
            (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved) &&
            r.StartDate <= input.EndDate && r.EndDate >= input.StartDate);
        if (overlapsLeave)
        {
            return ServiceError.Conflict("LEAVE_REQUEST_OVERLAP", "Există deja o cerere de concediu activă în această perioadă.");
        }

        var overlapsRemote = await db.RemoteRequestDays.AnyAsync(d =>
            d.Date >= input.StartDate && d.Date <= input.EndDate &&
            db.RemoteRequests.Any(r => r.Id == d.RemoteRequestId && r.UserId == user.Id &&
                (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved)));
        if (overlapsRemote)
        {
            return ServiceError.Conflict("LEAVE_REMOTE_OVERLAP", "Există deja o cerere de remote work activă în această perioadă.");
        }

        // Regula 3: soldul disponibil (doar pentru odihnă).
        if (leaveType == LeaveType.Vacation &&
            await balances.ValidateAvailableAsync(user.Id, workingDates, requestId) is { } balanceError)
        {
            return balanceError;
        }

        // Regula 6: limita de absență a echipei (concediul medical nu poate fi planificat, deci nu e blocat).
        if (leaveType != LeaveType.Medical &&
            await ValidateTeamAbsenceAsync(user.TeamId, user.Id, workingDates, request.TeamAbsenceLimitPercent) is { } teamError)
        {
            return teamError;
        }

        request.StartDate = input.StartDate;
        request.EndDate = input.EndDate;
        request.WorkingDates = workingDates;
        request.Type = leaveType;
        request.Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (existing is null) db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();

        // O cerere nouă îi anunță pe cei care o pot aproba (editarea nu schimbă starea, deci nu notifică).
        if (existing is null) await notifications.LeaveSubmittedAsync(request, user);

        return new SavedLeaveRequestView(request.Id, request.UserId, request.StartDate, request.EndDate,
            request.Type.ToString(), request.Status.ToString(), workingDates.Count, request.Comment);
    }

    public async Task<Result<List<PendingLeaveView>>> GetPendingAsync(User reviewer)
    {
        if (!ReviewPolicy.IsReviewer(reviewer)) return ServiceError.Forbidden();

        var requesters = db.Users.AsNoTracking().ReviewableBy(reviewer);
        var pending = await (
            from leave in db.LeaveRequests.AsNoTracking()
            join requester in requesters on leave.UserId equals requester.Id
            where leave.Status == RequestStatus.Pending
            orderby leave.CreatedAt, leave.Id
            select new { Leave = leave, requester.FirstName, requester.LastName })
            .ToListAsync();

        return pending.Select(x => new PendingLeaveView(
            x.Leave.Id, x.Leave.UserId, x.FirstName, x.LastName, x.Leave.StartDate, x.Leave.EndDate,
            x.Leave.Type.ToString(), x.Leave.Status.ToString(), x.Leave.WorkingDates.Count,
            x.Leave.Comment, x.Leave.CreatedAt)).ToList();
    }

    public async Task<Result<RequestOutcomeView>> ApproveAsync(User reviewer, int id)
    {
        var request = await db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound(ownedOnly: false);

        var requester = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (ReviewPolicy.Check(reviewer, requester, isApproval: true) is { } denied) return denied;

        if (request.Status != RequestStatus.Pending) return NotPending("aprobate");

        // Limita echipei se verifică din nou: între timp pot fi aprobate alte concedii.
        if (request.Type != LeaveType.Medical &&
            await ValidateTeamAbsenceAsync(requester!.TeamId, requester.Id, request.WorkingDates,
                request.TeamAbsenceLimitPercent) is { } teamError)
        {
            return teamError;
        }

        var updated = await db.LeaveRequests
            .Where(r => r.Id == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.Approved));
        if (updated == 0) return NotPending("aprobate");

        notifications.LeaveApproved(request, requester!, reviewer);
        return new RequestOutcomeView(id, nameof(RequestStatus.Approved), null, "Cererea de concediu a fost aprobată.");
    }

    public async Task<Result<RequestOutcomeView>> RejectAsync(User reviewer, int id, string? reason)
    {
        var request = await db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (request is null) return NotFound(ownedOnly: false);

        var requester = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (ReviewPolicy.Check(reviewer, requester, isApproval: false) is { } denied) return denied;

        if (request.Status != RequestStatus.Pending) return NotPending("respinse");

        // Regula 8: motiv obligatoriu, minimum 10 caractere.
        var trimmed = reason?.Trim();
        if (!ApiInput.HasValidRejectionReason(trimmed))
        {
            return ServiceError.BadRequest("INVALID_REJECTION_REASON", "Motivul respingerii trebuie să conțină minimum 10 caractere.");
        }

        var updated = await db.LeaveRequests
            .Where(r => r.Id == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RequestStatus.Rejected)
                .SetProperty(r => r.RejectionReason, trimmed));
        if (updated == 0) return NotPending("respinse");

        notifications.LeaveRejected(request, requester!, reviewer, trimmed!);
        return new RequestOutcomeView(id, nameof(RequestStatus.Rejected), trimmed, "Cererea de concediu a fost respinsă.");
    }

    /// <summary>Cerința 10: o cerere în așteptare se anulează oricând; una aprobată, doar înainte de data de început.</summary>
    public async Task<Result<RequestOutcomeView>> CancelAsync(User user, int id)
    {
        var owned = db.LeaveRequests.Where(r => r.Id == id && r.UserId == user.Id);
        var request = await owned.AsNoTracking().FirstOrDefaultAsync();
        if (request is null) return NotFound(ownedOnly: true);

        if (!CanCancel(request, clock.Today))
        {
            return request.Status == RequestStatus.Approved
                ? ServiceError.Conflict("LEAVE_ALREADY_STARTED", "O cerere aprobată poate fi anulată doar înainte de data de început.")
                : ServiceError.Conflict("INVALID_REQUEST_STATUS", "Cererea nu mai poate fi anulată.");
        }

        var previousStatus = request.Status;
        var updated = await owned
            .Where(r => r.Status == previousStatus)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RequestStatus.Canceled));
        if (updated == 0) return ServiceError.Conflict("INVALID_REQUEST_STATUS", "Cererea nu mai poate fi anulată.");

        await notifications.LeaveCancelledAsync(request, user, wasApproved: previousStatus == RequestStatus.Approved);
        return new RequestOutcomeView(id, nameof(RequestStatus.Canceled), null, "Cererea de concediu a fost anulată.");
    }

    public static bool CanCancel(LeaveRequest request, DateOnly today) =>
        request.Status == RequestStatus.Pending ||
        (request.Status == RequestStatus.Approved && request.StartDate > today);

    /// <summary>
    /// Regula 6: pentru fiecare zi, (colegi cu concediu aprobat + solicitantul) / membri activi
    /// nu poate depăși procentul maxim. Se numără doar concediile aprobate.
    /// </summary>
    private async Task<ServiceError?> ValidateTeamAbsenceAsync(
        int? teamId, int userId, List<DateOnly> workingDates, int maxAbsentPct)
    {
        if (!teamId.HasValue) return null;

        var memberIds = await db.Users.AsNoTracking()
            .Where(u => u.TeamId == teamId.Value && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();
        if (memberIds.Count == 0)
        {
            return ServiceError.Conflict("EMPTY_TEAM", "Echipa nu are membri activi.");
        }

        var start = workingDates.Min();
        var end = workingDates.Max();
        var approved = await db.LeaveRequests.AsNoTracking()
            .Where(r => memberIds.Contains(r.UserId) && r.UserId != userId &&
                r.Status == RequestStatus.Approved && r.StartDate <= end && r.EndDate >= start)
            .Select(r => new { r.UserId, r.WorkingDates })
            .ToListAsync();

        foreach (var date in workingDates)
        {
            var absent = approved.Where(r => r.WorkingDates.Contains(date)).Select(r => r.UserId).Distinct().Count();
            if ((absent + 1) * 100.0 / memberIds.Count > maxAbsentPct)
            {
                return ServiceError.Conflict("TEAM_ABSENCE_LIMIT_EXCEEDED",
                    $"Limita de absențe a echipei ar fi depășită în data de {date:yyyy-MM-dd}.");
            }
        }
        return null;
    }

    private static ServiceError NotPending(string action) =>
        ServiceError.Conflict("INVALID_REQUEST_STATUS", $"Doar cererile în așteptare pot fi {action}.");
}
