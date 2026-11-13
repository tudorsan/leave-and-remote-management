using System.Security.Claims;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/leave-balances")]
public class LeaveBalancesController(AppDbContext db, LeaveBalanceService balances) : ControllerBase
{
    [HttpGet("me")]
    public Task<IActionResult> GetOwnBalance([FromQuery] int? year) => GetBalance(null, year);

    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetBalance(int? userId, [FromQuery] int? year)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser == null) return ApiErrors.Unauthenticated();

        var targetId = userId ?? currentUser.Id;
        if (targetId != currentUser.Id)
        {
            if (currentUser.Role == UserRole.Employee) return ApiErrors.Forbidden();
            var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetId);
            if (target == null) return ApiErrors.NotFound("USER_NOT_FOUND", "Utilizatorul nu există.");
            // Managerul vede soldurile echipei sale și ale subordonaților direcți (ale căror cereri le aprobă).
            if (currentUser.Role == UserRole.Manager && target.ManagerId != currentUser.Id &&
                (!currentUser.TeamId.HasValue || currentUser.TeamId != target.TeamId)) return ApiErrors.Forbidden();
        }

        var currentYear = DateTime.Today.Year;
        var requestedYear = year ?? currentYear;
        if (requestedYear is < 1 or > 9999) return InvalidYear();

        var usage = await balances.GetUsageAsync(targetId, requestedYear);
        if (requestedYear < currentYear && usage.Used == 0 && usage.Pending == 0 &&
            !await db.AnnualLeaveBalances.AnyAsync(b => b.UserId == targetId && b.Year == requestedYear))
        {
            return NotFound(new
            {
                code = "LEAVE_BALANCE_UNAVAILABLE",
                message = $"Informații indisponibile pentru anul {requestedYear}."
            });
        }

        var allocation = await balances.GetAllocationAsync(targetId, requestedYear);
        if (!allocation.HasValue)
        {
            return StatusCode(500, new { code = "INVALID_LEAVE_SETTINGS", message = "Soldul anual implicit nu este configurat corect." });
        }
        return BalanceResponse(targetId, requestedYear, allocation.Value, usage);
    }

    [HttpPut("{userId:int}/{year:int}")]
    [Authorize(Roles = "Admin")]
    [RequestWriteTransaction]
    public async Task<IActionResult> SetBalance(int userId, int year, [FromBody] SetLeaveBalanceRequest request)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser == null) return ApiErrors.Unauthenticated();
        if (currentUser.Role != UserRole.Admin) return ApiErrors.Forbidden();
        if (year is < 1 or > 9999) return InvalidYear();
        if (!request.TotalDays.HasValue || request.TotalDays.Value < 0)
        {
            return BadRequest(new { code = "INVALID_ANNUAL_DAYS", message = "Numărul de zile trebuie să fie un întreg pozitiv sau zero." });
        }
        var targetUser = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (targetUser == null) return ApiErrors.NotFound("USER_NOT_FOUND", "Utilizatorul nu există.");

        var usage = await balances.GetUsageAsync(userId, year);
        if (request.TotalDays.Value < usage.Used + usage.Pending)
        {
            return Conflict(new { code = "BALANCE_BELOW_RESERVED", message = "Alocarea nu poate fi mai mică decât zilele consumate și rezervate." });
        }

        var allocation = await db.AnnualLeaveBalances.SingleOrDefaultAsync(b => b.UserId == userId && b.Year == year);
        if (allocation == null)
        {
            allocation = new AnnualLeaveBalance { UserId = userId, Year = year };
            db.AnnualLeaveBalances.Add(allocation);
        }
        allocation.TotalDays = request.TotalDays.Value;
        // Maximul este o proprietate a angajatului: setarea pentru anul curent sau unul viitor
        // devine valoarea moștenită de anii următori fără alocare explicită.
        if (year >= DateTime.Today.Year) targetUser.DefaultAnnualLeaveDays = request.TotalDays.Value;
        await db.SaveChangesAsync();
        return BalanceResponse(userId, year, allocation.TotalDays, usage);
    }

    private IActionResult BalanceResponse(int userId, int year, int allocation, (int Used, int Pending) usage)
    {
        var (used, pending) = usage;
        return Ok(new
        {
            UserId = userId,
            Year = year,
            TotalDays = allocation,
            UsedDays = used,
            PendingDays = pending,
            AvailableDays = allocation - used - pending
        });
    }

    private Task<User?> GetCurrentUserAsync() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive)
            : Task.FromResult<User?>(null);

    private BadRequestObjectResult InvalidYear() =>
        BadRequest(new { code = "INVALID_YEAR", message = "Anul este invalid." });
}

public record SetLeaveBalanceRequest(int? TotalDays);
