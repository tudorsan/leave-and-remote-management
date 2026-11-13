using System.Net;
using System.Net.Http.Json;
using LeaveRemote.Api.Services;
using LeaveRemote.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>
/// Sărbătorile legale sunt generate automat pentru toți anii în care se pot face cereri,
/// fără repornirea aplicației și fără să suprascrie modificările Adminului.
/// </summary>
public class PublicHolidayGenerationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    private async Task<List<int>> EnsureOnAsync(DateOnly today)
    {
        factory.SetToday(today);
        try
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PublicHolidayService>().EnsureCoveredAsync();
        }
        finally
        {
            factory.ResetToday();
        }
    }

    private async Task<List<string>> HolidayDatesAsync() =>
        (await (await _api.AdminAsync()).Client.GetJson("/api/settings")).GetProperty("holidays")
            .EnumerateArray().Select(h => h.Str("date")).ToList();

    [Fact]
    public async Task A_new_year_brings_the_holidays_of_the_following_year_without_a_restart()
    {
        await _api.UpdateSettingsAsync();
        Assert.DoesNotContain("2028-01-01", await HolidayDatesAsync());

        var generated = await EnsureOnAsync(new DateOnly(2027, 1, 2));

        Assert.Contains(2028, generated);
        var dates = await HolidayDatesAsync();
        Assert.Contains("2028-01-01", dates);
        Assert.Contains("2028-04-16", dates); // Paștele ortodox 2028
    }

    [Fact]
    public async Task A_holiday_removed_by_the_admin_is_not_added_back()
    {
        var admin = await _api.AdminAsync();
        var holidays = (await admin.Client.GetJson("/api/settings")).GetProperty("holidays").EnumerateArray()
            .Select(h => new { date = h.Str("date"), name = h.Str("name") }).ToList();
        var withoutEpiphany = holidays.Where(h => h.date != "2027-01-06").ToList();
        await (await admin.Client.PutAsJsonAsync("/api/settings/holidays", withoutEpiphany)).ShouldHaveStatus(HttpStatusCode.NoContent);
        try
        {
            await EnsureOnAsync(ApiFactory.Today);

            Assert.DoesNotContain("2027-01-06", await HolidayDatesAsync());
        }
        finally
        {
            await (await admin.Client.PutAsJsonAsync("/api/settings/holidays", holidays)).ShouldHaveStatus(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Running_the_generation_again_creates_no_duplicates()
    {
        await EnsureOnAsync(ApiFactory.Today);
        var before = await HolidayDatesAsync();

        var generated = await EnsureOnAsync(ApiFactory.Today);

        Assert.Empty(generated);
        Assert.Equal(before, await HolidayDatesAsync());
    }
}

/// <summary>Într-o bază separată, ca anii generați aici să nu influențeze testul despre schimbarea anului.</summary>
public class PublicHolidayHorizonTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Holidays_cover_the_longest_horizon_allowed_by_the_settings()
    {
        await _api.UpdateSettingsAsync(leaveMaxDaysAhead: 800); // 13.11.2026 + 800 de zile = ianuarie 2029
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<PublicHolidayService>().EnsureCoveredAsync();
            }

            var dates = (await (await _api.AdminAsync()).Client.GetJson("/api/settings")).GetProperty("holidays")
                .EnumerateArray().Select(h => h.Str("date")).ToList();
            Assert.Contains("2029-01-24", dates);
        }
        finally
        {
            await _api.UpdateSettingsAsync();
        }
    }
}
