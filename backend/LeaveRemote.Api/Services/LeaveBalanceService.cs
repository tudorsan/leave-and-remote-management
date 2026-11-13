using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>
/// Soldul de concediu de odihnă pe an calendaristic:
/// disponibil = alocare − aprobate − în așteptare. Doar concediul de odihnă consumă sold.
/// </summary>
public sealed class LeaveBalanceService(AppDbContext db, AppClock clock)
{
    /// <summary>
    /// Alocarea pentru un an: valoarea setată explicit pentru acel an sau, în lipsa ei,
    /// maximul anual al angajatului (preluat din setarea globală la creare și actualizat de Admin).
    /// </summary>
    public async Task<int?> GetAllocationAsync(int userId, int year)
    {
        var allocation = await db.AnnualLeaveBalances.AsNoTracking()
            .Where(b => b.UserId == userId && b.Year == year)
            .Select(b => (int?)b.TotalDays)
            .FirstOrDefaultAsync();
        if (allocation.HasValue) return allocation;

        return await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.DefaultAnnualLeaveDays)
            .FirstOrDefaultAsync();
    }

    /// <summary>Zilele lucrătoare din <paramref name="year"/> consumate (aprobate) și blocate (în așteptare).</summary>
    public async Task<(int Used, int Pending)> GetUsageAsync(int userId, int year, int? excludeRequestId = null)
    {
        var requests = await db.LeaveRequests.AsNoTracking()
            .Where(r => r.UserId == userId && r.Type == LeaveType.Vacation &&
                (r.Status == RequestStatus.Approved || r.Status == RequestStatus.Pending) &&
                r.StartDate.Year <= year && r.EndDate.Year >= year &&
                (!excludeRequestId.HasValue || r.Id != excludeRequestId.Value))
            .Select(r => new { r.Status, r.WorkingDates })
            .ToListAsync();

        return (
            requests.Where(r => r.Status == RequestStatus.Approved).Sum(r => r.WorkingDates.Count(d => d.Year == year)),
            requests.Where(r => r.Status == RequestStatus.Pending).Sum(r => r.WorkingDates.Count(d => d.Year == year)));
    }

    /// <summary>Regula 3: cererea de odihnă nu poate depăși soldul disponibil în niciunul dintre anii pe care îi acoperă.</summary>
    public async Task<ServiceError?> ValidateAvailableAsync(int userId, IEnumerable<DateOnly> workingDates, int? excludeRequestId)
    {
        foreach (var year in workingDates.GroupBy(date => date.Year))
        {
            var allocation = await GetAllocationAsync(userId, year.Key);
            if (!allocation.HasValue)
            {
                return ServiceError.Internal("INVALID_LEAVE_SETTINGS", "Soldul anual implicit nu este configurat corect.");
            }

            var (used, pending) = await GetUsageAsync(userId, year.Key, excludeRequestId);
            var available = allocation.Value - used - pending;
            if (year.Count() > available)
            {
                return ServiceError.Conflict("INSUFFICIENT_LEAVE_BALANCE",
                    $"Sold insuficient pentru {year.Key}. Disponibile: {available} zile; solicitate: {year.Count()} zile.");
            }
        }
        return null;
    }

    /// <summary>
    /// Angajatul își vede doar soldul propriu; Managerul vede echipa sa și subordonații direcți; Adminul vede pe toți.
    /// </summary>
    public static bool CanView(User viewer, User target) => viewer.Role switch
    {
        _ when viewer.Id == target.Id => true,
        UserRole.Admin => true,
        UserRole.Manager => target.ManagerId == viewer.Id ||
            (viewer.TeamId.HasValue && viewer.TeamId == target.TeamId),
        _ => false
    };

    public async Task<Result<BalanceView>> GetBalanceAsync(User viewer, int? userId, int? year)
    {
        var targetId = userId ?? viewer.Id;
        if (targetId != viewer.Id)
        {
            if (viewer.Role == UserRole.Employee) return ServiceError.Forbidden();
            var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetId);
            if (target is null) return UserNotFound;
            if (!CanView(viewer, target)) return ServiceError.Forbidden();
        }

        var currentYear = clock.CurrentYear;
        var requestedYear = year ?? currentYear;
        if (requestedYear is < 1 or > 9999) return InvalidYear;

        var usage = await GetUsageAsync(targetId, requestedYear);
        if (requestedYear < currentYear && usage.Used == 0 && usage.Pending == 0 &&
            !await db.AnnualLeaveBalances.AnyAsync(b => b.UserId == targetId && b.Year == requestedYear))
        {
            return ServiceError.NotFound("LEAVE_BALANCE_UNAVAILABLE", $"Informații indisponibile pentru anul {requestedYear}.");
        }

        var allocation = await GetAllocationAsync(targetId, requestedYear);
        return allocation.HasValue
            ? ToView(targetId, requestedYear, allocation.Value, usage)
            : ServiceError.Internal("INVALID_LEAVE_SETTINGS", "Soldul anual implicit nu este configurat corect.");
    }

    /// <summary>
    /// Admin setează maximul anual al unui angajat. Pentru anul curent sau unul viitor,
    /// valoarea devine și maximul moștenit de anii următori fără alocare explicită.
    /// </summary>
    public async Task<Result<BalanceView>> SetAllocationAsync(int userId, int year, int? totalDays)
    {
        if (year is < 1 or > 9999) return InvalidYear;
        if (totalDays is null or < 0)
        {
            return ServiceError.BadRequest("INVALID_ANNUAL_DAYS", "Numărul de zile trebuie să fie un întreg pozitiv sau zero.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null) return UserNotFound;

        var usage = await GetUsageAsync(userId, year);
        if (totalDays.Value < usage.Used + usage.Pending)
        {
            return ServiceError.Conflict("BALANCE_BELOW_RESERVED", "Alocarea nu poate fi mai mică decât zilele consumate și rezervate.");
        }

        var allocation = await db.AnnualLeaveBalances.SingleOrDefaultAsync(b => b.UserId == userId && b.Year == year);
        if (allocation is null)
        {
            allocation = new AnnualLeaveBalance { UserId = userId, Year = year };
            db.AnnualLeaveBalances.Add(allocation);
        }
        allocation.TotalDays = totalDays.Value;
        if (year >= clock.CurrentYear) user.DefaultAnnualLeaveDays = totalDays.Value;
        await db.SaveChangesAsync();

        return ToView(userId, year, allocation.TotalDays, usage);
    }

    private static BalanceView ToView(int userId, int year, int total, (int Used, int Pending) usage) =>
        new(userId, year, total, usage.Used, usage.Pending, total - usage.Used - usage.Pending);

    private static ServiceError UserNotFound => ServiceError.NotFound("USER_NOT_FOUND", "Utilizatorul nu există.");

    private static ServiceError InvalidYear => ServiceError.BadRequest("INVALID_YEAR", "Anul este invalid.");
}
