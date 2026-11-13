
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

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

        // Sărbătorile legale sunt generate de PublicHolidayService (la pornire și periodic).

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

}
