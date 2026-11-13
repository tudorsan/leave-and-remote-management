using System.Globalization;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>Sărbătorile legale din România pentru un an: cele fixe și cele legate de Paștele ortodox.</summary>
public static class RomanianPublicHolidays
{
    private static readonly (int Month, int Day, string Name)[] Fixed =
    [
        (1, 1, "Anul Nou"),
        (1, 2, "A doua zi de Anul Nou"),
        (1, 6, "Boboteaza"),
        (1, 7, "Sfântul Ioan Botezătorul"),
        (1, 24, "Ziua Unirii Principatelor Române"),
        (5, 1, "Ziua Muncii"),
        (6, 1, "Ziua Copilului"),
        (8, 15, "Adormirea Maicii Domnului"),
        (11, 30, "Sfântul Andrei"),
        (12, 1, "Ziua Națională a României"),
        (12, 25, "Crăciunul"),
        (12, 26, "A doua zi de Crăciun")
    ];

    public static List<PublicHoliday> For(int year)
    {
        var easter = OrthodoxEaster(year);
        var movable = new[]
        {
            (easter.AddDays(-2), "Vinerea Mare"),
            (easter, "Paștele Ortodox"),
            (easter.AddDays(1), "A doua zi de Paște"),
            (easter.AddDays(49), "Rusaliile"),
            (easter.AddDays(50), "A doua zi de Rusalii")
        };

        // Când două sărbători cad în aceeași zi (ex. Rusaliile și Ziua Copilului), devin o singură zi liberă.
        return Fixed.Select(h => (Date: new DateOnly(year, h.Month, h.Day), h.Name))
            .Concat(movable.Select(h => (Date: h.Item1, Name: h.Item2)))
            .GroupBy(h => h.Date)
            .OrderBy(h => h.Key)
            .Select(h => new PublicHoliday { Date = h.Key, Name = string.Join(" / ", h.Select(x => x.Name)) })
            .ToList();
    }

    /// <summary>Paștele ortodox: calculat în calendarul iulian (algoritmul Meeus), apoi convertit în gregorian.</summary>
    public static DateOnly OrthodoxEaster(int year)
    {
        var paschalMoon = (19 * (year % 19) + 15) % 30;
        var sunday = (2 * (year % 4) + 4 * (year % 7) - paschalMoon + 34) % 7;
        var date = paschalMoon + sunday + 114;
        return DateOnly.FromDateTime(new JulianCalendar().ToDateTime(year, date / 31, date % 31 + 1, 0, 0, 0, 0));
    }
}

/// <summary>
/// Se asigură că există sărbătorile legale pentru toți anii în care se pot face cereri:
/// de la anul curent până la anul celei mai îndepărtate zile permise de setări (concediu sau remote).
/// Rulează la pornire și periodic, deci la schimbarea anului noul an este generat fără repornire.
/// Fiecare an este generat automat o singură dată, ca modificările făcute de Admin să fie respectate.
/// </summary>
public sealed class PublicHolidayService(AppDbContext db, SettingsService settings, AppClock clock, ILogger<PublicHolidayService> logger)
{
    private const string SeededKeyPrefix = SettingKeys.InternalPrefix + "holidays_seeded.";
    private const int MaxYearsAhead = 10;

    /// <summary>Întoarce anii pentru care au fost generate sărbători acum.</summary>
    public async Task<List<int>> EnsureCoveredAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        // Limită de siguranță: cel mult 10 ani în avans, chiar dacă setările ar permite mai mult.
        var maxDaysAhead = await MaxDaysAheadAsync();
        var lastDayNumber = (int)Math.Min((long)today.DayNumber + maxDaysAhead, DateOnly.MaxValue.DayNumber);
        var lastYear = Math.Min(DateOnly.FromDayNumber(lastDayNumber).Year, Math.Min(today.Year + MaxYearsAhead, 9999));
        var generated = new List<int>();

        for (var year = today.Year; year <= lastYear; year++)
        {
            var seededKey = SeededKeyPrefix + year;
            if (await db.AppSettings.AnyAsync(s => s.Key == seededKey, cancellationToken)) continue;

            db.AppSettings.Add(new AppSetting { Key = seededKey, Value = "true" });
            // Dacă anul are deja sărbători (introduse de Admin), nu le dublăm.
            if (!await db.PublicHolidays.AnyAsync(h => h.Date.Year == year, cancellationToken))
            {
                db.PublicHolidays.AddRange(RomanianPublicHolidays.For(year));
                generated.Add(year);
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (generated.Count > 0)
        {
            logger.LogInformation("Au fost generate sărbătorile legale pentru: {Years}.", string.Join(", ", generated));
        }
        return generated;
    }

    private async Task<int> MaxDaysAheadAsync()
    {
        var leave = await settings.GetLeaveRulesAsync();
        var remote = await settings.GetRemoteRulesAsync();
        return Math.Max(leave.Value?.MaxDaysAhead ?? 365, remote.Value?.MaxDaysAhead ?? 30);
    }
}
