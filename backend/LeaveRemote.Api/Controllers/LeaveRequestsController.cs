using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LeaveRemote.Api.Controllers;

[ApiController]
[Route("api/leave-requests")]
[Authorize]
public class LeaveRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LeaveBalanceService _balances;

    public LeaveRequestsController(AppDbContext db, LeaveBalanceService balances)
    {
        _db = db;
        _balances = balances;
    }

    [HttpGet]
    public async Task<IActionResult> GetLeaveRequests()
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var userId))
        {
            return Unauthorized(new { code = "INVALID_TOKEN", message = "Identificatorul utilizatorului este invalid." });
        }

        var currentUser = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (currentUser == null)
        {
            return Unauthorized(new { code = "USER_INACTIVE", message = "Contul utilizatorului este inactiv." });
        }

        var requests = await _db.LeaveRequests.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        return Ok(requests.Select(r => new
        {
            r.Id, r.UserId, r.StartDate, r.EndDate,
            Type = r.Type.ToString(),
            Status = r.Status.ToString(),
            WorkingDays = r.WorkingDates.Count,
            r.Comment, r.RejectionReason, r.CreatedAt,
            CanCancel = r.Status == RequestStatus.Pending ||
                (r.Status == RequestStatus.Approved && r.StartDate > today)
        }));
    }

    [HttpPost]
    [RequestWriteTransaction]
    public Task<IActionResult> CreateLeaveRequest([FromBody] CreateLeaveRequest request) =>
        SaveLeaveRequestAsync(request, null);

    [HttpPut("{id:int}")]
    [RequestWriteTransaction]
    public Task<IActionResult> EditLeaveRequest(int id, [FromBody] CreateLeaveRequest request) =>
        SaveLeaveRequestAsync(request, id);

    private async Task<IActionResult> SaveLeaveRequestAsync(CreateLeaveRequest request, int? requestId)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var userId))
        {
            return Unauthorized(new { code = "INVALID_TOKEN", message = "Identificatorul utilizatorului este invalid." });
        }

        var currentUser = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (currentUser == null)
        {
            return Unauthorized(new { code = "USER_INACTIVE", message = "Contul utilizatorului este inactiv." });
        }

        LeaveRequest? existing = null;
        if (requestId.HasValue)
        {
            existing = await _db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == userId);
            if (existing == null)
            {
                return NotFound(new { code = "LEAVE_REQUEST_NOT_FOUND", message = "Cererea de concediu nu există sau nu îți aparține." });
            }
            if (existing.Status != RequestStatus.Pending)
            {
                return Conflict(new { code = "INVALID_REQUEST_STATUS", message = "Doar cererile în așteptare pot fi editate." });
            }
        }

        if (request.StartDate == default || request.EndDate == default ||
            request.StartDate > request.EndDate || request.EndDate == DateOnly.MaxValue)
        {
            return BadRequest(new { code = "INVALID_DATE_RANGE", message = "Intervalul de concediu este invalid." });
        }

        if (!ApiInput.TryParseEnumName<LeaveType>(request.Type, out var leaveType))
        {
            return BadRequest(new { code = "INVALID_LEAVE_TYPE", message = "Tipul concediului este invalid." });
        }

        var leaveRequest = existing ?? new LeaveRequest { UserId = userId };
        var scheduleChanged = existing == null || existing.StartDate != request.StartDate ||
            existing.EndDate != request.EndDate || existing.Type != leaveType;
        if (existing == null)
        {
            var settings = await _db.AppSettings.AsNoTracking()
                .Where(s => s.Key == "leave.min_days_ahead" || s.Key == "leave.max_days_ahead" ||
                    s.Key == "leave.max_team_absent_pct")
                .ToDictionaryAsync(s => s.Key, s => s.Value);

            if (!int.TryParse(settings.GetValueOrDefault("leave.min_days_ahead"), out var minDays) || minDays < 0 ||
                !int.TryParse(settings.GetValueOrDefault("leave.max_days_ahead"), out var maxDays) || maxDays < minDays ||
                !int.TryParse(settings.GetValueOrDefault("leave.max_team_absent_pct"), out var maxAbsentPct) ||
                maxAbsentPct is < 0 or > 100)
            {
                return StatusCode(500, new { code = "INVALID_LEAVE_SETTINGS", message = "Setările pentru concedii sunt invalide." });
            }

            leaveRequest.MinDaysAhead = minDays;
            leaveRequest.MaxDaysAhead = maxDays;
            leaveRequest.TeamAbsenceLimitPercent = maxAbsentPct;
            leaveRequest.HolidayDates = await _db.PublicHolidays.AsNoTracking().Select(h => h.Date).ToListAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysAhead = request.StartDate.DayNumber - today.DayNumber;
        // Minimul configurat se aplica numai concediului de odihna.
        var effectiveMinDays = leaveType == LeaveType.Vacation ? leaveRequest.MinDaysAhead : 0;
        if (daysAhead < effectiveMinDays || request.EndDate.DayNumber - today.DayNumber > leaveRequest.MaxDaysAhead)
        {
            return BadRequest(new
            {
                code = "LEAVE_DATE_OUT_OF_RANGE",
                message = $"Concediul trebuie să înceapă cu minimum {effectiveMinDays} zile în avans și să se termine în maximum {leaveRequest.MaxDaysAhead} zile de acum."
            });
        }

        var workingDates = !scheduleChanged && existing != null
            ? existing.WorkingDates
            : GetWorkingDates(request.StartDate, request.EndDate, leaveRequest.HolidayDates);
        if (workingDates.Count == 0)
        {
            return BadRequest(new { code = "NO_WORKING_DAYS", message = "Intervalul nu conține zile lucrătoare." });
        }

        var overlappingLeave = await _db.LeaveRequests.AnyAsync(r =>
            r.UserId == userId && (!requestId.HasValue || r.Id != requestId.Value) &&
            (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved) &&
            r.StartDate <= request.EndDate && r.EndDate >= request.StartDate);
        if (overlappingLeave)
        {
            return Conflict(new { code = "LEAVE_REQUEST_OVERLAP", message = "Există deja o cerere de concediu activă în această perioadă." });
        }

        var overlappingRemote = await _db.RemoteRequestDays.AnyAsync(d =>
            d.Date >= request.StartDate && d.Date <= request.EndDate &&
            _db.RemoteRequests.Any(r => r.Id == d.RemoteRequestId && r.UserId == userId &&
                (r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved)));
        if (overlappingRemote)
        {
            return Conflict(new { code = "LEAVE_REMOTE_OVERLAP", message = "Există deja o cerere de remote work activă în această perioadă." });
        }

        if (leaveType == LeaveType.Vacation)
        {
            var balanceError = await ValidateVacationBalanceAsync(userId, workingDates, requestId);
            if (balanceError != null) return balanceError;
        }

        // Concediul medical nu poate fi planificat, deci nu este blocat de limita echipei
        // (dar, odată aprobat, contează ca absență pentru ceilalți).
        if (leaveType != LeaveType.Medical)
        {
            var teamError = await ValidateTeamAbsenceAsync(
                currentUser.TeamId, userId, workingDates, leaveRequest.TeamAbsenceLimitPercent);
            if (teamError != null) return teamError;
        }

        leaveRequest.StartDate = request.StartDate;
        leaveRequest.EndDate = request.EndDate;
        leaveRequest.WorkingDates = workingDates;
        leaveRequest.Type = leaveType;
        leaveRequest.Comment = request.Comment?.Trim();
        if (existing == null) _db.LeaveRequests.Add(leaveRequest);
        await _db.SaveChangesAsync();

        var response = new
        {
            leaveRequest.Id, leaveRequest.UserId, leaveRequest.StartDate, leaveRequest.EndDate,
            Type = leaveRequest.Type.ToString(),
            Status = leaveRequest.Status.ToString(),
            WorkingDays = workingDates.Count,
            leaveRequest.Comment
        };
        return existing == null ? Created($"/api/leave-requests/{leaveRequest.Id}", response) : Ok(response);
    }

    [HttpGet("pending")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> GetPendingLeaveRequests()
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var reviewerId))
        {
            return Unauthorized(new { code = "INVALID_TOKEN", message = "Identificatorul utilizatorului este invalid." });
        }

        var reviewer = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == reviewerId && u.IsActive);
        if (reviewer == null)
        {
            return Unauthorized(new { code = "USER_INACTIVE", message = "Contul utilizatorului este inactiv." });
        }

        if (reviewer.Role != UserRole.Manager && reviewer.Role != UserRole.Admin)
        {
            return StatusCode(403, new { code = "FORBIDDEN", message = "Nu ai permisiunea să consulți cererile în așteptare." });
        }

        var query =
            from leave in _db.LeaveRequests.AsNoTracking()
            join requester in _db.Users.AsNoTracking() on leave.UserId equals requester.Id
            where leave.Status == RequestStatus.Pending && requester.Id != reviewerId
            select new { Leave = leave, Requester = requester };

        if (reviewer.Role == UserRole.Manager)
        {
            var teamId = reviewer.TeamId;
            query = query.Where(x => x.Requester.IsActive && x.Requester.Id != reviewerId && (x.Requester.Role == UserRole.Manager
                ? x.Requester.ManagerId == reviewerId
                : teamId.HasValue && x.Requester.TeamId == teamId));
        }

        var pending = await query
            .OrderBy(x => x.Leave.CreatedAt)
            .ThenBy(x => x.Leave.Id)
            .Select(x => new
            {
                x.Leave.Id,
                x.Leave.UserId,
                x.Requester.FirstName,
                x.Requester.LastName,
                x.Leave.StartDate,
                x.Leave.EndDate,
                Type = x.Leave.Type.ToString(),
                Status = x.Leave.Status.ToString(),
                x.Leave.Comment,
                x.Leave.CreatedAt
            })
            .ToListAsync();

        return Ok(pending);
    }

    [HttpPatch("{id:int}/approve")]
    [RequestWriteTransaction]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> ApproveLeaveRequest(int id)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var approverId))
        {
            return Unauthorized(new { code = "INVALID_TOKEN", message = "Identificatorul utilizatorului este invalid." });
        }

        var approver = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == approverId && u.IsActive);
        if (approver == null)
        {
            return Unauthorized(new { code = "USER_INACTIVE", message = "Contul utilizatorului este inactiv." });
        }
        if (approver.Role != UserRole.Manager && approver.Role != UserRole.Admin)
        {
            return StatusCode(403, new { code = "FORBIDDEN", message = "Nu ai permisiunea să aprobi concedii." });
        }

        var leaveRequest = await _db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (leaveRequest == null)
        {
            return NotFound(new { code = "LEAVE_REQUEST_NOT_FOUND", message = "Cererea de concediu nu există." });
        }

        var employee = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == leaveRequest.UserId);
        if (employee == null || (!employee.IsActive && approver.Role != UserRole.Admin))
        {
            return Conflict(new { code = "EMPLOYEE_INACTIVE", message = "Angajatul nu există sau este inactiv." });
        }

        var permissionError = ValidateReviewPermission(approver, employee, isApproval: true);
        if (permissionError != null) return permissionError;

        if (leaveRequest.Status != RequestStatus.Pending)
        {
            return Conflict(new { code = "INVALID_REQUEST_STATUS", message = "Doar cererile Pending pot fi aprobate." });
        }

        if (leaveRequest.WorkingDates.Count == 0)
        {
            return Conflict(new { code = "NO_WORKING_DAYS", message = "Cererea nu conține zile lucrătoare." });
        }

        if (leaveRequest.Type != LeaveType.Medical)
        {
            var teamError = await ValidateTeamAbsenceAsync(
                employee.TeamId, employee.Id, leaveRequest.WorkingDates, leaveRequest.TeamAbsenceLimitPercent);
            if (teamError != null) return teamError;
        }

        var updated = await _db.LeaveRequests
            .Where(r => r.Id == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, RequestStatus.Approved));
        if (updated == 0)
        {
            return Conflict(new { code = "INVALID_REQUEST_STATUS", message = "Doar cererile Pending pot fi aprobate." });
        }
        return Ok(new
        {
            leaveRequest.Id,
            Status = RequestStatus.Approved.ToString(),
            message = "Cererea de concediu a fost aprobată."
        });
    }

    [HttpPatch("{id:int}/reject")]
    [RequestWriteTransaction]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> RejectLeaveRequest(int id, [FromBody] RejectLeaveRequestDto request)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var reviewerId))
        {
            return Unauthorized(new { code = "INVALID_TOKEN", message = "Identificatorul utilizatorului este invalid." });
        }

        var reviewer = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == reviewerId && u.IsActive);
        if (reviewer == null)
        {
            return Unauthorized(new { code = "USER_INACTIVE", message = "Contul utilizatorului este inactiv." });
        }
        if (reviewer.Role != UserRole.Manager && reviewer.Role != UserRole.Admin)
        {
            return StatusCode(403, new { code = "FORBIDDEN", message = "Nu ai permisiunea să respingi concedii." });
        }

        var leaveRequest = await _db.LeaveRequests.FirstOrDefaultAsync(r => r.Id == id);
        if (leaveRequest == null)
        {
            return NotFound(new { code = "LEAVE_REQUEST_NOT_FOUND", message = "Cererea de concediu nu există." });
        }

        var employee = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == leaveRequest.UserId);
        if (employee == null || (!employee.IsActive && reviewer.Role != UserRole.Admin))
        {
            return Conflict(new { code = "EMPLOYEE_INACTIVE", message = "Angajatul nu există sau este inactiv." });
        }

        var permissionError = ValidateReviewPermission(reviewer, employee, isApproval: false);
        if (permissionError != null) return permissionError;

        if (leaveRequest.Status != RequestStatus.Pending)
        {
            return Conflict(new { code = "INVALID_REQUEST_STATUS", message = "Doar cererile Pending pot fi respinse." });
        }

        var reason = request?.Reason?.Trim();
        if (!ApiInput.HasValidRejectionReason(reason))
        {
            return BadRequest(new { code = "INVALID_REJECTION_REASON", message = "Motivul respingerii trebuie să conțină minimum 10 caractere." });
        }

        var updated = await _db.LeaveRequests
            .Where(r => r.Id == id && r.Status == RequestStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, RequestStatus.Rejected)
                .SetProperty(r => r.RejectionReason, reason));
        if (updated == 0)
        {
            return Conflict(new { code = "INVALID_REQUEST_STATUS", message = "Doar cererile Pending pot fi respinse." });
        }
        return Ok(new
        {
            leaveRequest.Id,
            Status = RequestStatus.Rejected.ToString(),
            RejectionReason = reason,
            message = "Cererea de concediu a fost respinsă."
        });
    }

    [HttpPatch("{id:int}/cancel")]
    [RequestWriteTransaction]
    public async Task<IActionResult> CancelLeaveRequest(int id)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdText, out var userId))
        {
            return Unauthorized(new { code = "INVALID_TOKEN", message = "Identificatorul utilizatorului este invalid." });
        }

        if (!await _db.Users.AnyAsync(u => u.Id == userId && u.IsActive))
        {
            return Unauthorized(new { code = "USER_INACTIVE", message = "Contul utilizatorului este inactiv." });
        }

        var ownedRequest = _db.LeaveRequests.Where(r => r.Id == id && r.UserId == userId);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var updated = await ownedRequest
            .Where(r => r.Status == RequestStatus.Pending ||
                (r.Status == RequestStatus.Approved && r.StartDate > today))
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, RequestStatus.Canceled));

        if (updated > 0)
        {
            return Ok(new { Id = id, Status = RequestStatus.Canceled.ToString(), message = "Cererea de concediu a fost anulată." });
        }

        var leaveRequest = await ownedRequest.AsNoTracking().FirstOrDefaultAsync();
        if (leaveRequest == null)
        {
            return NotFound(new { code = "LEAVE_REQUEST_NOT_FOUND", message = "Cererea de concediu nu există sau nu îți aparține." });
        }

        if (leaveRequest.Status == RequestStatus.Approved && leaveRequest.StartDate <= today)
        {
            return Conflict(new { code = "LEAVE_ALREADY_STARTED", message = "O cerere aprobată poate fi anulată doar înainte de data de început." });
        }

        return Conflict(new { code = "INVALID_REQUEST_STATUS", message = "Cererea nu mai poate fi anulată." });
    }

    private IActionResult? ValidateReviewPermission(User reviewer, User requester, bool isApproval)
    {
        // Nimeni, nici Adminul, nu își poate analiza propria cerere (regula 7).
        if (reviewer.Id == requester.Id)
        {
            return Conflict(new
            {
                code = isApproval ? "SELF_APPROVAL_FORBIDDEN" : "SELF_REJECTION_FORBIDDEN",
                message = isApproval ? "Nu îți poți aproba propria cerere." : "Nu îți poți respinge propria cerere."
            });
        }
        if (reviewer.Role == UserRole.Admin) return null;
        if (reviewer.Role != UserRole.Manager)
        {
            return StatusCode(403, new { code = "FORBIDDEN", message = "Nu ai permisiunea să analizezi această cerere." });
        }
        if (requester.Role == UserRole.Manager)
        {
            if (requester.ManagerId != reviewer.Id)
            {
                return StatusCode(403, new
                {
                    code = "NOT_DIRECT_MANAGER",
                    message = "Doar superiorul direct sau Admin poate analiza cererea unui Manager."
                });
            }
            return null;
        }
        if (!reviewer.TeamId.HasValue || requester.TeamId != reviewer.TeamId)
        {
            return StatusCode(403, new { code = "NOT_YOUR_TEAM", message = "Poți analiza doar cererile angajaților din echipa ta." });
        }
        return null;
    }

    // Soldul disponibil = total - Approved - Pending, fără cererea editată.
    private async Task<IActionResult?> ValidateVacationBalanceAsync(
        int userId,
        List<DateOnly> workingDates,
        int? requestId)
    {
        foreach (var year in workingDates.GroupBy(date => date.Year))
        {
            var allocation = await _balances.GetAllocationAsync(userId, year.Key);
            if (!allocation.HasValue)
            {
                return StatusCode(500, new { code = "INVALID_LEAVE_SETTINGS", message = "Soldul anual implicit nu este configurat corect." });
            }

            var usage = await _balances.GetUsageAsync(userId, year.Key, requestId);
            var availableDays = allocation.Value - usage.Used - usage.Pending;
            if (year.Count() > availableDays)
            {
                return Conflict(new
                {
                    code = "INSUFFICIENT_LEAVE_BALANCE",
                    message = $"Sold insuficient pentru {year.Key}. Disponibile: {availableDays} zile; solicitate: {year.Count()} zile."
                });
            }
        }
        return null;
    }

    private async Task<IActionResult?> ValidateTeamAbsenceAsync(
        int? teamId, int userId, List<DateOnly> workingDates, int maxAbsentPct)
    {
        if (!teamId.HasValue) return null;

        var teamMemberIds = await _db.Users.AsNoTracking()
            .Where(u => u.TeamId == teamId.Value && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();
        var teamSize = teamMemberIds.Count;
        if (teamSize == 0)
        {
            return Conflict(new { code = "EMPTY_TEAM", message = "Echipa nu are membri activi." });
        }

        var startDate = workingDates.Min();
        var endDate = workingDates.Max();
        var approved = await _db.LeaveRequests.AsNoTracking()
            .Where(r => teamMemberIds.Contains(r.UserId) && r.UserId != userId &&
                r.Status == RequestStatus.Approved && r.StartDate <= endDate && r.EndDate >= startDate)
            .Select(r => new { r.UserId, r.WorkingDates })
            .ToListAsync();

        foreach (var date in workingDates)
        {
            var absentCount = approved.Where(r => r.WorkingDates.Contains(date))
                .Select(r => r.UserId)
                .Distinct()
                .Count();
            var absentPct = (absentCount + 1) * 100.0 / teamSize;
            if (absentPct > maxAbsentPct)
            {
                return Conflict(new
                {
                    code = "TEAM_ABSENCE_LIMIT_EXCEEDED",
                    message = $"Limita de absențe a echipei ar fi depășită în data de {date:yyyy-MM-dd}."
                });
            }
        }
        return null;
    }

    private static List<DateOnly> GetWorkingDates(DateOnly startDate, DateOnly endDate, List<DateOnly> holidays)
    {
        var holidaySet = holidays.ToHashSet();
        var workingDates = new List<DateOnly>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var isWeekend = date.DayOfWeek == DayOfWeek.Saturday ||
                            date.DayOfWeek == DayOfWeek.Sunday;
            if (!isWeekend && !holidaySet.Contains(date)) workingDates.Add(date);
        }
        return workingDates;
    }
}

public class CreateLeaveRequest
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Comment { get; set; }
}

public class RejectLeaveRequestDto
{
    public string? Reason { get; set; }
}
