
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LeaveRemote.Api.Controllers;

[ApiController]
[Route("api/remote-requests")]
[Authorize]
public class RemoteRequestsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RemoteRequestsController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/remote-requests
    // Utilizatorul isi vede propriile cereri remote.
    [HttpGet]
    public async Task<IActionResult> GetMyRequests()
    {
        var (user, error) = await GetCurrentUserAsync();
        if (error != null) return error;

        var requests = await _db.RemoteRequests
            .AsNoTracking()
            .Include(r => r.Days)
            .Where(r => r.UserId == user!.Id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.Today);
        return Ok(requests.Select(r => new
        {
            r.Id,
            r.UserId,
            Status = r.Status.ToString(),
            Days = r.Days.Select(d => d.Date)
                .OrderBy(d => d)
                .ToList(),
            r.Comment,
            r.RejectionReason,
            r.CreatedAt,
            CanEdit = r.Status == RequestStatus.Pending,
            CanCancel = CanCancel(r, today)
        }));
    }

    // POST /api/remote-requests
    // Creeaza o cerere pentru una sau mai multe zile.
    [HttpPost]
    [RequestWriteTransaction]
    public Task<IActionResult> CreateRequest([FromBody] CreateRemoteRequestDto request) =>
        SaveRemoteRequestAsync(request, null);

    [HttpPut("{id:int}")]
    [RequestWriteTransaction]
    public Task<IActionResult> EditRequest(int id, [FromBody] CreateRemoteRequestDto request) =>
        SaveRemoteRequestAsync(request, id);

    private async Task<IActionResult> SaveRemoteRequestAsync(CreateRemoteRequestDto request, int? requestId)
    {
        var (user, error) = await GetCurrentUserAsync();
        if (error != null) return error;

        var today = DateOnly.FromDateTime(DateTime.Today);
        RemoteRequest? existing = null;
        if (requestId.HasValue)
        {
            existing = await _db.RemoteRequests.Include(r => r.Days)
                .FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == user!.Id);
            if (existing == null)
            {
                return NotFound(new { code = "REMOTE_REQUEST_NOT_FOUND", message = "Cererea remote nu există sau nu îți aparține." });
            }
            if (existing.Status != RequestStatus.Pending)
            {
                return Conflict(new
                {
                    code = "INVALID_REQUEST_STATUS",
                    message = "Doar cererile în așteptare pot fi editate."
                });
            }
        }

        if (request?.Days == null || request.Days.Count == 0)
        {
            return BadRequest(new
            {
                code = "REMOTE_DAYS_REQUIRED",
                message = "Selectează cel puțin o zi remote."
            });
        }

        var days = request.Days
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (days.Any(d => d == default))
        {
            return BadRequest(new
            {
                code = "INVALID_REMOTE_DATE",
                message = "Una dintre datele selectate este invalidă."
            });
        }

        // Fiecare zi remote este o cerere separată (start = end), deci editarea privește o singură zi.
        if (existing != null && days.Count != 1)
        {
            return BadRequest(new
            {
                code = "REMOTE_EDIT_SINGLE_DAY",
                message = "O cerere remote acoperă o singură zi; la editare selectează exact o zi."
            });
        }

        var remoteRequest = existing ?? new RemoteRequest { UserId = user!.Id };
        var scheduleChanged = existing == null || !days.SequenceEqual(existing.Days.Select(day => day.Date).Order());
        if (existing == null)
        {
            var settings = await _db.AppSettings.AsNoTracking()
                .Where(s => s.Key.StartsWith("remote."))
                .ToDictionaryAsync(s => s.Key, s => s.Value);

            if (!int.TryParse(settings.GetValueOrDefault("remote.min_days_ahead"), out var minDays) || minDays < 0 ||
                !int.TryParse(settings.GetValueOrDefault("remote.max_days_ahead"), out var maxDays) || maxDays < minDays ||
                !int.TryParse(settings.GetValueOrDefault("remote.max_days_per_week"), out var weeklyLimit) || weeklyLimit is < 0 or > 7 ||
                !bool.TryParse(settings.GetValueOrDefault("remote.requires_approval"), out var requiresApproval))
            {
                return StatusCode(500, new { code = "INVALID_REMOTE_SETTINGS", message = "Setările pentru remote sunt invalide." });
            }

            remoteRequest.MinDaysAhead = minDays;
            remoteRequest.MaxDaysAhead = maxDays;
            remoteRequest.MaxDaysPerWeek = weeklyLimit;
            remoteRequest.Status = requiresApproval ? RequestStatus.Pending : RequestStatus.Approved;
            remoteRequest.WasAutoApproved = !requiresApproval;
            remoteRequest.HolidayDates = await _db.PublicHolidays.AsNoTracking().Select(h => h.Date).ToListAsync();
        }

        foreach (var day in days)
        {
            var daysAhead = day.DayNumber - today.DayNumber;

            if (daysAhead < remoteRequest.MinDaysAhead || daysAhead > remoteRequest.MaxDaysAhead)
            {
                return BadRequest(new
                {
                    code = "REMOTE_DATE_OUT_OF_RANGE",
                    message = $"Ziua {day:yyyy-MM-dd} este în afara intervalului permis: între {remoteRequest.MinDaysAhead} și {remoteRequest.MaxDaysAhead} zile de acum."
                });
            }

            if (day.DayOfWeek == DayOfWeek.Saturday ||
                day.DayOfWeek == DayOfWeek.Sunday)
            {
                return BadRequest(new
                {
                    code = "REMOTE_WEEKEND_FORBIDDEN",
                    message = $"Ziua {day:yyyy-MM-dd} este în weekend."
                });
            }
        }

        var holidaySet = remoteRequest.HolidayDates.ToHashSet();

        foreach (var day in days)
        {
            if (holidaySet.Contains(day))
            {
                return BadRequest(new
                {
                    code = "REMOTE_HOLIDAY_FORBIDDEN",
                    message = $"Ziua {day:yyyy-MM-dd} este sărbătoare legală."
                });
            }
        }

        // Nu permitem suprapunerea cu un concediu activ.
        var overlappingLeave = await _db.LeaveRequests
            .AsNoTracking()
            .AnyAsync(r =>
                r.UserId == user!.Id &&
                (r.Status == RequestStatus.Pending ||
                 r.Status == RequestStatus.Approved) &&
                days.Any(d => d >= r.StartDate && d <= r.EndDate));

        if (overlappingLeave)
        {
            return Conflict(new
            {
                code = "REMOTE_LEAVE_OVERLAP",
                message = "Una dintre zilele selectate se suprapune cu un concediu activ."
            });
        }

        // Nu permitem suprapunerea cu alta cerere remote activa.
        var overlappingRemote = await _db.RemoteRequestDays
            .AsNoTracking()
            .AnyAsync(d =>
                days.Contains(d.Date) && (!requestId.HasValue || d.RemoteRequestId != requestId.Value) &&
                _db.RemoteRequests.Any(r =>
                    r.Id == d.RemoteRequestId &&
                    r.UserId == user!.Id &&
                    (r.Status == RequestStatus.Pending ||
                     r.Status == RequestStatus.Approved)));

        if (overlappingRemote)
        {
            return Conflict(new
            {
                code = "REMOTE_REQUEST_OVERLAP",
                message = "Una dintre zilele selectate are deja o cerere remote activă."
            });
        }

        if (scheduleChanged)
        {
            var weeklyError = await ValidateWeeklyLimitAsync(user!.Id, days, remoteRequest.MaxDaysPerWeek, requestId);
            if (weeklyError != null) return weeklyError;
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (existing != null)
        {
            existing.Comment = comment;
            existing.Days.RemoveAll(d => !days.Contains(d.Date));
            foreach (var date in days.Where(date => !existing.Days.Any(d => d.Date == date)))
            {
                existing.Days.Add(new RemoteRequestDay { Date = date });
            }
            await _db.SaveChangesAsync();
            return Ok(new
            {
                existing.Id,
                existing.UserId,
                Status = existing.Status.ToString(),
                Days = days,
                existing.Comment
            });
        }

        // Toate zilele sunt validate împreună (tot sau nimic), apoi se salvează câte o cerere per zi,
        // astfel încât fiecare zi poate fi aprobată, respinsă sau anulată independent.
        var created = days.Select(date => new RemoteRequest
        {
            UserId = remoteRequest.UserId,
            Status = remoteRequest.Status,
            WasAutoApproved = remoteRequest.WasAutoApproved,
            Comment = comment,
            MinDaysAhead = remoteRequest.MinDaysAhead,
            MaxDaysAhead = remoteRequest.MaxDaysAhead,
            MaxDaysPerWeek = remoteRequest.MaxDaysPerWeek,
            HolidayDates = remoteRequest.HolidayDates,
            Days = [new RemoteRequestDay { Date = date }]
        }).ToList();
        _db.RemoteRequests.AddRange(created);
        await _db.SaveChangesAsync();

        return Created("/api/remote-requests", new
        {
            Ids = created.Select(r => r.Id).ToList(),
            UserId = remoteRequest.UserId,
            Status = remoteRequest.Status.ToString(),
            Days = days,
            Comment = comment
        });
    }

    // GET /api/remote-requests/pending
    // Managerul vede cererile din echipa sa.
    // Adminul vede toate cererile Pending.
    [HttpGet("pending")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> GetPendingRequests()
    {
        var (reviewer, error) = await GetCurrentUserAsync();
        if (error != null) return error;

        if (reviewer!.Role != UserRole.Manager &&
            reviewer.Role != UserRole.Admin)
        {
            return Forbidden();
        }

        var query =
            from remote in _db.RemoteRequests.AsNoTracking()
            join requester in _db.Users.AsNoTracking()
                on remote.UserId equals requester.Id
            where remote.Status == RequestStatus.Pending && requester.Id != reviewer.Id
            select new
            {
                Remote = remote,
                Requester = requester
            };

        if (reviewer.Role == UserRole.Manager)
        {
            query = query.Where(x =>
                x.Requester.IsActive && x.Requester.Id != reviewer.Id && (x.Requester.Role == UserRole.Manager
                    ? x.Requester.ManagerId == reviewer.Id
                    : reviewer.TeamId.HasValue &&
                      x.Requester.TeamId == reviewer.TeamId));
        }

        var pending = await query
            .Select(x => new
            {
                x.Remote.Id,
                x.Remote.UserId,
                FirstName = x.Requester.FirstName,
                LastName = x.Requester.LastName,
                x.Remote.Comment,
                x.Remote.CreatedAt,
                Days = _db.RemoteRequestDays
                    .Where(d => d.RemoteRequestId == x.Remote.Id)
                    .Select(d => d.Date)
                    .OrderBy(d => d)
                    .ToList()
            })
            .ToListAsync();

        return Ok(pending);
    }

    // PATCH /api/remote-requests/{id}/approve
    [HttpPatch("{id:int}/approve")]
    [RequestWriteTransaction]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> ApproveRequest(int id)
    {
        var (reviewer, error) = await GetCurrentUserAsync();
        if (error != null) return error;

        if (reviewer!.Role != UserRole.Manager &&
            reviewer.Role != UserRole.Admin)
        {
            return Forbidden();
        }

        var remoteRequest = await _db.RemoteRequests
            .Include(r => r.Days)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (remoteRequest == null)
        {
            return NotFound(new
            {
                code = "REMOTE_REQUEST_NOT_FOUND",
                message = "Cererea remote nu există."
            });
        }

        var requester = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == remoteRequest.UserId);

        var permissionError = ValidateReviewPermission(
            reviewer, requester, isApproval: true);

        if (permissionError != null) return permissionError;

        if (remoteRequest.Status != RequestStatus.Pending)
        {
            return Conflict(new
            {
                code = "INVALID_REQUEST_STATUS",
                message = "Doar cererile Pending pot fi aprobate."
            });
        }

        var weeklyError = await ValidateWeeklyLimitAsync(remoteRequest.UserId,
            remoteRequest.Days.Select(day => day.Date), remoteRequest.MaxDaysPerWeek, id);
        if (weeklyError != null) return weeklyError;

        remoteRequest.Status = RequestStatus.Approved;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            remoteRequest.Id,
            Status = remoteRequest.Status.ToString(),
            message = "Cererea remote a fost aprobată."
        });
    }

    // PATCH /api/remote-requests/{id}/reject
    [HttpPatch("{id:int}/reject")]
    [RequestWriteTransaction]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> RejectRequest(
        int id,
        [FromBody] RejectRemoteRequestDto request)
    {
        var (reviewer, error) = await GetCurrentUserAsync();
        if (error != null) return error;

        if (reviewer!.Role != UserRole.Manager &&
            reviewer.Role != UserRole.Admin)
        {
            return Forbidden();
        }

        var remoteRequest = await _db.RemoteRequests
            .FirstOrDefaultAsync(r => r.Id == id);

        if (remoteRequest == null)
        {
            return NotFound(new
            {
                code = "REMOTE_REQUEST_NOT_FOUND",
                message = "Cererea remote nu există."
            });
        }

        var requester = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == remoteRequest.UserId);

        var permissionError = ValidateReviewPermission(
            reviewer, requester, isApproval: false);

        if (permissionError != null) return permissionError;

        if (remoteRequest.Status != RequestStatus.Pending)
        {
            return Conflict(new
            {
                code = "INVALID_REQUEST_STATUS",
                message = "Doar cererile Pending pot fi respinse."
            });
        }

        var reason = request?.Reason?.Trim();

        if (!ApiInput.HasValidRejectionReason(reason))
        {
            return BadRequest(new
            {
                code = "INVALID_REJECTION_REASON",
                message = "Motivul respingerii trebuie să conțină minimum 10 caractere."
            });
        }

        remoteRequest.Status = RequestStatus.Rejected;
        remoteRequest.RejectionReason = reason;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            remoteRequest.Id,
            Status = remoteRequest.Status.ToString(),
            remoteRequest.RejectionReason,
            message = "Cererea remote a fost respinsă."
        });
    }

    // PATCH /api/remote-requests/{id}/cancel
    // Pending se poate anula.
    // Approved se poate anula numai inainte de prima zi.
    [HttpPatch("{id:int}/cancel")]
    [RequestWriteTransaction]
    public async Task<IActionResult> CancelRequest(int id)
    {
        var (user, error) = await GetCurrentUserAsync();
        if (error != null) return error;

        var remoteRequest = await _db.RemoteRequests
            .Include(r => r.Days)
            .FirstOrDefaultAsync(r =>
                r.Id == id &&
                r.UserId == user!.Id);

        if (remoteRequest == null)
        {
            return NotFound(new
            {
                code = "REMOTE_REQUEST_NOT_FOUND",
                message = "Cererea remote nu există sau nu îți aparține."
            });
        }

        if (remoteRequest.Status != RequestStatus.Pending &&
            remoteRequest.Status != RequestStatus.Approved)
        {
            return Conflict(new
            {
                code = "INVALID_REQUEST_STATUS",
                message = "Cererea nu mai poate fi anulată."
            });
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        if (!CanCancel(remoteRequest, today))
        {
            return Conflict(new
            {
                code = "REMOTE_ALREADY_STARTED",
                message = "O cerere aprobată poate fi anulată doar înainte de prima zi."
            });
        }

        remoteRequest.Status = RequestStatus.Canceled;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            remoteRequest.Id,
            Status = remoteRequest.Status.ToString(),
            message = "Cererea remote a fost anulată."
        });
    }

    // Regula de autorizare este identica pentru
    // aprobarea si respingerea cererilor remote.
    private IActionResult? ValidateReviewPermission(
        User reviewer,
        User? requester,
        bool isApproval)
    {
        if (requester == null || (!requester.IsActive && reviewer.Role != UserRole.Admin))
        {
            return Conflict(new
            {
                code = "REQUESTER_INACTIVE",
                message = "Solicitantul nu există sau este inactiv."
            });
        }

        // Nimeni, nici Adminul, nu își poate analiza propria cerere (regula 7).
        if (reviewer.Id == requester.Id)
        {
            return Conflict(new
            {
                code = isApproval ? "SELF_APPROVAL_FORBIDDEN" : "SELF_REJECTION_FORBIDDEN",
                message = isApproval ? "Nu îți poți aproba propria cerere." : "Nu îți poți respinge propria cerere."
            });
        }

        if (reviewer.Role == UserRole.Admin)
        {
            return null;
        }

        if (reviewer.Role != UserRole.Manager)
        {
            return Forbidden();
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

        if (!reviewer.TeamId.HasValue ||
            requester.TeamId != reviewer.TeamId)
        {
            return StatusCode(403, new
            {
                code = "NOT_YOUR_TEAM",
                message = "Poți analiza doar cererile angajaților din echipa ta."
            });
        }

        return null;
    }

    private async Task<(User? User, IActionResult? Error)>
        GetCurrentUserAsync()
    {
        var userIdText = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdText, out var userId))
        {
            return (null, Unauthorized(new
            {
                code = "INVALID_TOKEN",
                message = "Identificatorul utilizatorului este invalid."
            }));
        }

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u =>
                u.Id == userId && u.IsActive);

        if (user == null)
        {
            return (null, Unauthorized(new
            {
                code = "USER_INACTIVE",
                message = "Contul utilizatorului este inactiv."
            }));
        }

        return (user, null);
    }

    private IActionResult Forbidden()
    {
        return StatusCode(403, new
        {
            code = "FORBIDDEN",
            message = "Nu ai permisiunea pentru această operație."
        });
    }

    private async Task<IActionResult?> ValidateWeeklyLimitAsync(
        int userId, IEnumerable<DateOnly> days, int limit, int? requestId)
    {
        foreach (var week in days.GroupBy(GetMonday))
        {
            var monday = week.Key;
            var sunday = DateOnly.FromDayNumber(Math.Min(monday.DayNumber + 6, DateOnly.MaxValue.DayNumber));

            var existingDays = await _db.RemoteRequestDays
                .AsNoTracking()
                .Where(d =>
                    d.Date >= monday &&
                    d.Date <= sunday &&
                    (!requestId.HasValue || d.RemoteRequestId != requestId.Value) &&
                    _db.RemoteRequests.Any(r =>
                        r.Id == d.RemoteRequestId &&
                        r.UserId == userId &&
                        r.Status == RequestStatus.Approved))
                .Select(d => d.Date)
                .Distinct()
                .CountAsync();

            if (existingDays + week.Count() > limit)
            {
                return Conflict(new
                {
                    code = "REMOTE_WEEKLY_LIMIT_EXCEEDED",
                    message = $"Limita de {limit} zile remote pe săptămână ar fi depășită pentru săptămâna care începe la {monday:yyyy-MM-dd}."
                });
            }
        }
        return null;
    }

    private static bool CanCancel(RemoteRequest request, DateOnly today) =>
        request.Status == RequestStatus.Pending ||
        (request.Status == RequestStatus.Approved && request.Days.All(day => day.Date > today));

    private static DateOnly GetMonday(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}

public class CreateRemoteRequestDto
{
    public List<DateOnly> Days { get; set; } = new();

    public string? Comment { get; set; }
}

public class RejectRemoteRequestDto
{
    public string? Reason { get; set; }
}
