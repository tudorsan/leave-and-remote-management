using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Data;

public sealed class LeaveBalanceService(AppDbContext db)
{
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
}
