
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace LeaveRemote.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, bool seedDemoUsers, int currentYear)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Echipele initiale
        if (!await db.Teams.AnyAsync())
        {
            db.Teams.AddRange(
                new Team { Name = "Development" },
                new Team { Name = "QA" },
                new Team { Name = "Design" }
            );

            await db.SaveChangesAsync();
        }

        // Setarile globale initiale
        var initializeSettings = !await db.AppSettings.AnyAsync();
        if (initializeSettings)
        {
            db.AppSettings.AddRange(
                new AppSetting { Key = "remote.max_days_ahead", Value = "30" },
                new AppSetting { Key = "remote.min_days_ahead", Value = "0" },
                new AppSetting { Key = "remote.max_days_per_week", Value = "2" },
                new AppSetting { Key = "remote.requires_approval", Value = "false" },
                new AppSetting { Key = "leave.max_days_ahead", Value = "365" },
                new AppSetting { Key = "leave.min_days_ahead", Value = "3" },
                new AppSetting { Key = "leave.default_annual_days", Value = "21" },
                new AppSetting { Key = "leave.max_team_absent_pct", Value = "50" }
            );

            await db.SaveChangesAsync();
        }

        for (var year = currentYear; year <= Math.Min(currentYear + 1, 9999); year++)
        {
            var seedKey = $"internal.holidays_seeded.{year}";
            if (await db.AppSettings.AnyAsync(s => s.Key == seedKey)) continue;

            db.AppSettings.Add(new AppSetting { Key = seedKey, Value = "true" });
            // Existing installations may have customized or cleared the current year's calendar.
            if ((!initializeSettings && year == currentYear) ||
                await db.PublicHolidays.AnyAsync(h => h.Date.Year == year))
            {
                await db.SaveChangesAsync();
                continue;
            }

            var holidays = new (int Month, int Day, string Name)[]
            {
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
            };

            var easter = OrthodoxEaster(year);
            var movable = new[]
            {
                (easter.AddDays(-2), "Vinerea Mare"),
                (easter, "Paștele Ortodox"),
                (easter.AddDays(1), "A doua zi de Paște"),
                (easter.AddDays(49), "Rusaliile"),
                (easter.AddDays(50), "A doua zi de Rusalii")
            };
            db.PublicHolidays.AddRange(holidays.Select(h => (new DateOnly(year, h.Month, h.Day), h.Name))
                .Concat(movable)
                .GroupBy(h => h.Item1)
                .Select(h => new PublicHoliday { Date = h.Key, Name = string.Join(" / ", h.Select(x => x.Item2)) }));

            await db.SaveChangesAsync();
        }

        // Utilizatorii initiali
        if (seedDemoUsers && !await db.Users.AnyAsync())
        {
            var defaultDaysText = await db.AppSettings.Where(s => s.Key == "leave.default_annual_days")
                .Select(s => s.Value).SingleAsync();
            if (!int.TryParse(defaultDaysText, out var defaultDays) || defaultDays < 0)
            {
                throw new InvalidOperationException("Setarea leave.default_annual_days este invalidă.");
            }

            var developmentTeam = await db.Teams
                .FirstAsync(t => t.Name == "Development");

            // 1. Cream mai intai Adminul pentru a obtine ID-ul lui.
            var admin = new User
            {
                FirstName = "Alex",
                LastName = "Admin",
                Email = "admin@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                Role = UserRole.Admin,
                DefaultAnnualLeaveDays = defaultDays,
                IsActive = true
            };

            db.Users.Add(admin);
            await db.SaveChangesAsync();

            // 2. Cream Managerul cu echipa si superiorul atribuite.
            var manager = new User
            {
                FirstName = "Maria",
                LastName = "Manager",
                Email = "manager@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Manager123!"),
                Role = UserRole.Manager,
                DefaultAnnualLeaveDays = defaultDays,
                IsActive = true,
                TeamId = developmentTeam.Id,
                ManagerId = admin.Id
            };

            db.Users.Add(manager);
            await db.SaveChangesAsync();

            // 3. Cream angajatul care raporteaza catre Maria.
            var employee = new User
            {
                FirstName = "Andrei",
                LastName = "Employee",
                Email = "employee@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Employee123!"),
                Role = UserRole.Employee,
                DefaultAnnualLeaveDays = defaultDays,
                IsActive = true,
                TeamId = developmentTeam.Id,
                ManagerId = manager.Id
            };

            db.Users.AddRange(employee, new User
            {
                FirstName = "Ioana",
                LastName = "Employee",
                Email = "employee2@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Employee123!"),
                Role = UserRole.Employee,
                DefaultAnnualLeaveDays = defaultDays,
                TeamId = developmentTeam.Id,
                ManagerId = manager.Id
            });
            await db.SaveChangesAsync();
        }

        var unallocated = await db.Users.Where(u => !db.AnnualLeaveBalances.Any(b => b.UserId == u.Id))
            .Select(u => new { u.Id, u.DefaultAnnualLeaveDays }).ToListAsync();
        if (unallocated.Count > 0)
        {
            db.AnnualLeaveBalances.AddRange(unallocated.Select(u => new AnnualLeaveBalance
            {
                UserId = u.Id, Year = currentYear, TotalDays = u.DefaultAnnualLeaveDays
            }));
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
    }

    private static DateOnly OrthodoxEaster(int year)
    {
        // The Julian Easter calculation yields a Julian calendar date.
        var paschalMoon = (19 * (year % 19) + 15) % 30;
        var sunday = (2 * (year % 4) + 4 * (year % 7) - paschalMoon + 34) % 7;
        var date = paschalMoon + sunday + 114;
        return DateOnly.FromDateTime(new JulianCalendar().ToDateTime(year, date / 31, date % 31 + 1, 0, 0, 0, 0));
    }
}
