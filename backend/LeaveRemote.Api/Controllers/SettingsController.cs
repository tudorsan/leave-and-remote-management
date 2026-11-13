using System.Security.Claims;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/settings")]
public class SettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        if (!await IsCurrentAdminAsync()) return ApiErrors.Forbidden();

        return Ok(new
        {
            Settings = await db.AppSettings.AsNoTracking()
                .Where(s => !s.Key.StartsWith("internal."))
                .ToDictionaryAsync(s => s.Key, s => s.Value),
            Holidays = await db.PublicHolidays.AsNoTracking().OrderBy(h => h.Date)
                .Select(h => new { h.Date, h.Name }).ToListAsync()
        });
    }

    [HttpPut]
    [RequestWriteTransaction]
    public async Task<IActionResult> UpdateSettings([FromBody] GlobalSettingsRequest request)
    {
        if (!await IsCurrentAdminAsync()) return ApiErrors.Forbidden();

        if (request.RemoteMinDaysAhead is null or < 0 ||
            request.RemoteMaxDaysAhead is null || request.RemoteMaxDaysAhead < request.RemoteMinDaysAhead ||
            request.RemoteMaxDaysPerWeek is null or < 0 or > 7 ||
            request.RemoteRequiresApproval is null ||
            request.LeaveMinDaysAhead is null or < 0 ||
            request.LeaveMaxDaysAhead is null || request.LeaveMaxDaysAhead < request.LeaveMinDaysAhead ||
            request.LeaveDefaultAnnualDays is null or < 0 ||
            request.LeaveMaxTeamAbsentPct is null or < 0 or > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_SETTINGS",
                message = "Completează toate setările. Limitele trebuie să fie pozitive sau zero, maximul cel puțin egal cu minimul, iar procentul între 0 și 100."
            });
        }

        var values = new Dictionary<string, string>
        {
            ["remote.min_days_ahead"] = request.RemoteMinDaysAhead.Value.ToString(),
            ["remote.max_days_ahead"] = request.RemoteMaxDaysAhead.Value.ToString(),
            ["remote.max_days_per_week"] = request.RemoteMaxDaysPerWeek.Value.ToString(),
            ["remote.requires_approval"] = request.RemoteRequiresApproval.Value ? "true" : "false",
            ["leave.min_days_ahead"] = request.LeaveMinDaysAhead.Value.ToString(),
            ["leave.max_days_ahead"] = request.LeaveMaxDaysAhead.Value.ToString(),
            ["leave.default_annual_days"] = request.LeaveDefaultAnnualDays.Value.ToString(),
            ["leave.max_team_absent_pct"] = request.LeaveMaxTeamAbsentPct.Value.ToString()
        };

        var settings = await db.AppSettings.Where(s => values.Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key);
        foreach (var (key, value) in values)
        {
            if (settings.TryGetValue(key, out var setting)) setting.Value = value;
            else db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }

        await db.SaveChangesAsync();
        return Ok(values);
    }

    [HttpPut("holidays")]
    [RequestWriteTransaction]
    public async Task<IActionResult> UpdateHolidays([FromBody] List<HolidaySetting?> holidays)
    {
        if (!await IsCurrentAdminAsync()) return ApiErrors.Forbidden();

        if (holidays.Any(h => h == null || h.Date == default || string.IsNullOrWhiteSpace(h.Name)) ||
            holidays.Select(h => h!.Date).Distinct().Count() != holidays.Count)
        {
            return BadRequest(new { code = "INVALID_HOLIDAYS", message = "Fiecare sărbătoare trebuie să aibă o dată unică și un nume." });
        }

        var dates = holidays.ToDictionary(h => h!.Date, h => h!.Name!.Trim());
        var existing = await db.PublicHolidays.ToDictionaryAsync(h => h.Date);
        db.PublicHolidays.RemoveRange(existing.Values.Where(h => !dates.ContainsKey(h.Date)));
        foreach (var (date, name) in dates)
        {
            if (existing.TryGetValue(date, out var holiday)) holiday.Name = name;
            else db.PublicHolidays.Add(new PublicHoliday { Date = date, Name = name });
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<bool> IsCurrentAdminAsync() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? db.Users.AnyAsync(u => u.Id == id && u.IsActive && u.Role == UserRole.Admin)
            : Task.FromResult(false);
}

public record GlobalSettingsRequest(
    int? RemoteMinDaysAhead,
    int? RemoteMaxDaysAhead,
    int? RemoteMaxDaysPerWeek,
    bool? RemoteRequiresApproval,
    int? LeaveMinDaysAhead,
    int? LeaveMaxDaysAhead,
    int? LeaveDefaultAnnualDays,
    int? LeaveMaxTeamAbsentPct);

public record HolidaySetting(DateOnly Date, string? Name);
