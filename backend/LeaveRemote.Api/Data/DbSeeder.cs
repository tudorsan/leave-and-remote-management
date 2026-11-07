
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!await db.Teams.AnyAsync())
        {
            db.Teams.AddRange(
                new Team { Name = "Development" },
                new Team { Name = "QA" },
                new Team { Name = "Design" }
            );

            await db.SaveChangesAsync();
        }

        
        if (!await db.AppSettings.AnyAsync())
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

        
        if (!await db.PublicHolidays.AnyAsync(h => h.Date.Year == 2026))
        {
            var holidays = new (int Month, int Day, string Name)[]
            {
                (1, 1, "Anul Nou"),
                (1, 2, "A doua zi de Anul Nou"),
                (1, 6, "Boboteaza"),
                (1, 7, "Sfântul Ioan Botezătorul"),
                (1, 24, "Ziua Unirii Principatelor Române"),
                (4, 10, "Vinerea Mare"),
                (4, 12, "Paștele Ortodox"),
                (4, 13, "A doua zi de Paște"),
                (5, 1, "Ziua Muncii"),
                (5, 31, "Rusaliile"),
                (6, 1, "A doua zi de Rusalii / Ziua Copilului"),
                (8, 15, "Adormirea Maicii Domnului"),
                (11, 30, "Sfântul Andrei"),
                (12, 1, "Ziua Națională a României"),
                (12, 25, "Crăciunul"),
                (12, 26, "A doua zi de Crăciun")
            };

            foreach (var (month, day, name) in holidays)
            {
                db.PublicHolidays.Add(new PublicHoliday
                {
                    Date = new DateOnly(2026, month, day),
                    Name = name
                });
            }

            await db.SaveChangesAsync();
        }

        
        if (!await db.Users.AnyAsync())
            {
            var developmentTeam = await db.Teams
                .FirstAsync(team => team.Name == "Development");

            var admin = new User
            {
                FirstName = "Alex",
                LastName = "Admin",
                Email = "admin@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                Role = UserRole.Admin
            };

            var manager = new User
            {
                FirstName = "Maria",
                LastName = "Manager",
                Email = "manager@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Manager123!"),
                Role = UserRole.Manager,
                TeamId = developmentTeam.Id
            };

            db.Users.AddRange(admin, manager);
            await db.SaveChangesAsync();

            var employee = new User
            {
                FirstName = "Andrei",
                LastName = "Employee",
                Email = "employee@example.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Employee123!"),
                Role = UserRole.Employee,
                TeamId = developmentTeam.Id,
                ManagerId = manager.Id
            };

            db.Users.Add(employee);
            await db.SaveChangesAsync();
        }

    }
}
