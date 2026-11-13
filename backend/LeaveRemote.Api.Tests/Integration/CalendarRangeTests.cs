using System.Net;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>Cerința 6 (opțională), prima parte: datele pentru vederea săptămânală a calendarului.</summary>
public class CalendarRangeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Without_dates_the_current_monday_to_sunday_week_is_returned()
    {
        var team = await _api.CreateTeamAsync(employees: 1);

        var week = await team.Employees[0].Client.GetJson("/api/calendar/range");

        // „Azi” este vineri, 13.11.2026.
        Assert.Equal(Day(11, 9), week.Str("from"));
        Assert.Equal(Day(11, 15), week.Str("to"));
        Assert.Equal(team.TeamId, week.Int("teamId"));
    }

    [Fact]
    public async Task A_week_spanning_two_months_includes_events_and_holidays_of_both()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];
        await employee.Client.CreateLeaveId(Day(11, 27), Day(12, 3), "Unpaid");

        var week = await team.Manager.Client.GetJson($"/api/calendar/range?from={Day(11, 30)}&to={Day(12, 6)}");

        var holidays = week.GetProperty("holidays").EnumerateArray().Select(h => h.Str("date")).ToList();
        Assert.Equal([Day(11, 30), Day(12, 1)], holidays);
        var days = week.GetProperty("events").EnumerateArray().Where(e => e.Int("userId") == employee.Id).Select(e => e.Str("date")).ToList();
        Assert.Equal([Day(12, 2), Day(12, 3)], days); // 30.11 și 1.12 sunt sărbători, deci nu sunt zile de concediu
    }

    [Fact]
    public async Task The_week_view_uses_the_same_filters_and_permissions_as_the_month_view()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var otherTeam = await _api.CreateTeamAsync(employees: 0);
        var employee = team.Employees[0].Client;
        await employee.CreateRemoteIds(Day(11, 17));
        await employee.CreateLeaveId(Day(11, 18), Day(11, 18), "Unpaid");
        var url = $"/api/calendar/range?from={Day(11, 16)}&to={Day(11, 22)}";

        var remoteOnly = await team.Manager.Client.GetJson(url + "&type=Remote");
        Assert.Equal("Remote", Assert.Single(remoteOnly.GetProperty("events").EnumerateArray()).Str("type"));

        await (await employee.GetAsync(url + $"&teamId={otherTeam.TeamId}")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
        var admin = await _api.AdminAsync();
        Assert.Equal(otherTeam.TeamId, (await admin.Client.GetJson(url + $"&teamId={otherTeam.TeamId}")).Int("teamId"));
    }

    [Theory]
    [InlineData("2026-11-22", "2026-11-16")] // sfârșit înainte de început
    [InlineData("2026-11-01", "2026-12-31")] // mai mult de 42 de zile
    public async Task Invalid_ranges_are_rejected(string from, string to)
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await (await employee.GetAsync($"/api/calendar/range?from={from}&to={to}")).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_CALENDAR_RANGE");
    }
}
