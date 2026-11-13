using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

public static class SettingKeys
{
    public const string RemoteMinDaysAhead = "remote.min_days_ahead";
    public const string RemoteMaxDaysAhead = "remote.max_days_ahead";
    public const string RemoteMaxDaysPerWeek = "remote.max_days_per_week";
    public const string RemoteRequiresApproval = "remote.requires_approval";
    public const string LeaveMinDaysAhead = "leave.min_days_ahead";
    public const string LeaveMaxDaysAhead = "leave.max_days_ahead";
    public const string LeaveDefaultAnnualDays = "leave.default_annual_days";
    public const string LeaveMaxTeamAbsentPct = "leave.max_team_absent_pct";

    /// <summary>Chei folosite intern (ex. evidența sărbătorilor deja populate); nu apar în ecranul de setări.</summary>
    public const string InternalPrefix = "internal.";
}

public sealed record LeaveRules(int MinDaysAhead, int MaxDaysAhead, int MaxTeamAbsentPct);

public sealed record RemoteRules(int MinDaysAhead, int MaxDaysAhead, int MaxDaysPerWeek, bool RequiresApproval);

/// <summary>
/// Setările globale (tabel cheie–valoare). Sunt citite din baza de date la fiecare validare,
/// deci o modificare făcută de Admin are efect imediat pentru cererile noi.
/// </summary>
public sealed class SettingsService(AppDbContext db)
{
    public async Task<Result<LeaveRules>> GetLeaveRulesAsync()
    {
        var values = await ReadAsync(SettingKeys.LeaveMinDaysAhead, SettingKeys.LeaveMaxDaysAhead, SettingKeys.LeaveMaxTeamAbsentPct);
        if (!TryInt(values, SettingKeys.LeaveMinDaysAhead, out var min) || min < 0 ||
            !TryInt(values, SettingKeys.LeaveMaxDaysAhead, out var max) || max < min ||
            !TryInt(values, SettingKeys.LeaveMaxTeamAbsentPct, out var pct) || pct is < 0 or > 100)
        {
            return ServiceError.Internal("INVALID_LEAVE_SETTINGS", "Setările pentru concedii sunt invalide.");
        }
        return new LeaveRules(min, max, pct);
    }

    public async Task<Result<RemoteRules>> GetRemoteRulesAsync()
    {
        var values = await ReadAsync(SettingKeys.RemoteMinDaysAhead, SettingKeys.RemoteMaxDaysAhead,
            SettingKeys.RemoteMaxDaysPerWeek, SettingKeys.RemoteRequiresApproval);
        if (!TryInt(values, SettingKeys.RemoteMinDaysAhead, out var min) || min < 0 ||
            !TryInt(values, SettingKeys.RemoteMaxDaysAhead, out var max) || max < min ||
            !TryInt(values, SettingKeys.RemoteMaxDaysPerWeek, out var weekly) || weekly is < 0 or > 7 ||
            !bool.TryParse(values.GetValueOrDefault(SettingKeys.RemoteRequiresApproval), out var requiresApproval))
        {
            return ServiceError.Internal("INVALID_REMOTE_SETTINGS", "Setările pentru remote sunt invalide.");
        }
        return new RemoteRules(min, max, weekly, requiresApproval);
    }

    public async Task<Result<int>> GetDefaultAnnualDaysAsync()
    {
        var values = await ReadAsync(SettingKeys.LeaveDefaultAnnualDays);
        return TryInt(values, SettingKeys.LeaveDefaultAnnualDays, out var days) && days >= 0
            ? days
            : ServiceError.Internal("INVALID_DEFAULT_ANNUAL_DAYS_SETTING", "Setarea leave.default_annual_days lipsește sau este invalidă.");
    }

    public Task<List<DateOnly>> GetHolidayDatesAsync() =>
        db.PublicHolidays.AsNoTracking().Select(h => h.Date).ToListAsync();

    public async Task<SettingsView> GetAllAsync() => new(
        await db.AppSettings.AsNoTracking()
            .Where(s => !s.Key.StartsWith(SettingKeys.InternalPrefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value),
        await db.PublicHolidays.AsNoTracking().OrderBy(h => h.Date)
            .Select(h => new HolidayView(h.Date, h.Name)).ToListAsync());

    public async Task<Result<Dictionary<string, string>>> UpdateAsync(GlobalSettingsInput input)
    {
        if (input.RemoteMinDaysAhead is null or < 0 ||
            input.RemoteMaxDaysAhead is null || input.RemoteMaxDaysAhead < input.RemoteMinDaysAhead ||
            input.RemoteMaxDaysPerWeek is null or < 0 or > 7 ||
            input.RemoteRequiresApproval is null ||
            input.LeaveMinDaysAhead is null or < 0 ||
            input.LeaveMaxDaysAhead is null || input.LeaveMaxDaysAhead < input.LeaveMinDaysAhead ||
            input.LeaveDefaultAnnualDays is null or < 0 ||
            input.LeaveMaxTeamAbsentPct is null or < 0 or > 100)
        {
            return ServiceError.BadRequest("INVALID_SETTINGS",
                "Completează toate setările. Limitele trebuie să fie pozitive sau zero, maximul cel puțin egal cu minimul, iar procentul între 0 și 100.");
        }

        var values = new Dictionary<string, string>
        {
            [SettingKeys.RemoteMinDaysAhead] = input.RemoteMinDaysAhead.Value.ToString(),
            [SettingKeys.RemoteMaxDaysAhead] = input.RemoteMaxDaysAhead.Value.ToString(),
            [SettingKeys.RemoteMaxDaysPerWeek] = input.RemoteMaxDaysPerWeek.Value.ToString(),
            [SettingKeys.RemoteRequiresApproval] = input.RemoteRequiresApproval.Value ? "true" : "false",
            [SettingKeys.LeaveMinDaysAhead] = input.LeaveMinDaysAhead.Value.ToString(),
            [SettingKeys.LeaveMaxDaysAhead] = input.LeaveMaxDaysAhead.Value.ToString(),
            [SettingKeys.LeaveDefaultAnnualDays] = input.LeaveDefaultAnnualDays.Value.ToString(),
            [SettingKeys.LeaveMaxTeamAbsentPct] = input.LeaveMaxTeamAbsentPct.Value.ToString()
        };

        var existing = await db.AppSettings.Where(s => values.Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key);
        foreach (var (key, value) in values)
        {
            if (existing.TryGetValue(key, out var setting)) setting.Value = value;
            else db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        await db.SaveChangesAsync();
        return values;
    }

    /// <summary>Înlocuiește lista de sărbători legale. Cererile existente își păstrează lista de la creare.</summary>
    public async Task<ServiceError?> ReplaceHolidaysAsync(List<HolidayInput?> holidays)
    {
        if (holidays.Any(h => h == null || h.Date == default || string.IsNullOrWhiteSpace(h.Name)) ||
            holidays.Select(h => h!.Date).Distinct().Count() != holidays.Count)
        {
            return ServiceError.BadRequest("INVALID_HOLIDAYS", "Fiecare sărbătoare trebuie să aibă o dată unică și un nume.");
        }

        var wanted = holidays.ToDictionary(h => h!.Date, h => h!.Name!.Trim());
        var existing = await db.PublicHolidays.ToDictionaryAsync(h => h.Date);
        db.PublicHolidays.RemoveRange(existing.Values.Where(h => !wanted.ContainsKey(h.Date)));
        foreach (var (date, name) in wanted)
        {
            if (existing.TryGetValue(date, out var holiday)) holiday.Name = name;
            else db.PublicHolidays.Add(new PublicHoliday { Date = date, Name = name });
        }
        await db.SaveChangesAsync();
        return null;
    }

    private Task<Dictionary<string, string>> ReadAsync(params string[] keys) =>
        db.AppSettings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value);

    private static bool TryInt(Dictionary<string, string> values, string key, out int value) =>
        int.TryParse(values.GetValueOrDefault(key), out value);
}
