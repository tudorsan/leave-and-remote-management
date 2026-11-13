using System.Net;
using System.Net.Http.Json;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>
/// Cerințele 11–13 (zile remote) și regulile 4–5, cu setările implicite:
/// orizont 0–30 zile, maximum 2 zile pe săptămână, fără aprobare. „Azi” este vineri, 13.11.2026.
/// </summary>
public class RemoteRequestTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    // Cerința 11 + 13
    [Fact]
    public async Task Several_days_in_one_form_become_one_auto_approved_request_per_day()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        var response = await employee.RequestRemote(Day(11, 17), Day(11, 19));

        await response.ShouldHaveStatus(HttpStatusCode.Created);
        var body = await response.Body();
        Assert.Equal("Approved", body.Str("status"));
        Assert.Equal(2, body.GetProperty("ids").GetArrayLength());
        var own = (await employee.GetJson("/api/remote-requests")).EnumerateArray().ToList();
        Assert.Equal(2, own.Count);
        Assert.All(own, r => Assert.Equal(1, r.GetProperty("days").GetArrayLength()));
    }

    // Cerința 12
    [Fact]
    public async Task Remote_days_can_be_scheduled_from_today_up_to_thirty_days_ahead()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await (await employee.RequestRemote(Day(11, 13))).ShouldHaveStatus(HttpStatusCode.Created);
        await (await employee.RequestRemote(Day(12, 11))).ShouldHaveStatus(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(11, 12)] // ieri
    [InlineData(12, 14)] // 31 de zile
    public async Task Days_outside_the_configured_horizon_are_refused_with_an_explicit_message(int month, int day)
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        var response = await employee.RequestRemote(Day(month, day));

        await response.ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_DATE_OUT_OF_RANGE");
        Assert.Contains("între 0 și 30 zile", (await response.Body()).Str("message"));
    }

    // Regula 4
    [Fact]
    public async Task Remote_cannot_fall_on_a_weekend()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await (await employee.RequestRemote(Day(11, 21))).ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_WEEKEND_FORBIDDEN");
    }

    [Fact]
    public async Task Remote_cannot_fall_on_a_public_holiday()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await (await employee.RequestRemote(Day(11, 30))).ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_HOLIDAY_FORBIDDEN");
    }

    [Fact]
    public async Task One_invalid_day_rejects_the_whole_form()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await (await employee.RequestRemote(Day(11, 17), Day(11, 21))).ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_WEEKEND_FORBIDDEN");

        Assert.Empty((await employee.GetJson("/api/remote-requests")).EnumerateArray());
    }

    [Fact]
    public async Task At_least_one_day_is_required()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await (await employee.RequestRemote()).ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_DAYS_REQUIRED");
    }

    // Regula 5
    [Fact]
    public async Task Weekly_limit_applies_to_the_monday_to_sunday_week_counting_approved_days()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await employee.CreateRemoteIds(Day(11, 16), Day(11, 17));

        await (await employee.RequestRemote(Day(11, 20))).ShouldFailWith(HttpStatusCode.Conflict, "REMOTE_WEEKLY_LIMIT_EXCEEDED");
        await (await employee.RequestRemote(Day(11, 23))).ShouldHaveStatus(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Weekly_limit_is_checked_for_every_week_of_a_multi_week_selection()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await (await employee.RequestRemote(Day(11, 19), Day(11, 20), Day(11, 23), Day(11, 24))).ShouldHaveStatus(HttpStatusCode.Created);
        await (await employee.RequestRemote(Day(11, 25), Day(12, 2))).ShouldFailWith(HttpStatusCode.Conflict, "REMOTE_WEEKLY_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Cancelled_days_free_up_the_weekly_limit()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        var ids = await employee.CreateRemoteIds(Day(11, 16), Day(11, 17));

        await (await employee.CancelRemote(ids[0])).ShouldHaveStatus(HttpStatusCode.OK);

        await (await employee.RequestRemote(Day(11, 18))).ShouldHaveStatus(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Each_remote_day_is_cancelled_independently_and_only_before_it_starts()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        var ids = await employee.CreateRemoteIds(Day(11, 16), Day(11, 17));

        factory.SetToday(new DateOnly(2026, 11, 16));
        try
        {
            await (await employee.CancelRemote(ids[0])).ShouldFailWith(HttpStatusCode.Conflict, "REMOTE_ALREADY_STARTED");
            await (await employee.CancelRemote(ids[1])).ShouldHaveStatus(HttpStatusCode.OK);
        }
        finally
        {
            factory.ResetToday();
        }
    }

    [Fact]
    public async Task Remote_days_appear_in_the_team_calendar_and_type_filter()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        await team.Employees[0].Client.CreateRemoteIds(Day(11, 18));
        await team.Employees[0].Client.CreateLeaveId(Day(11, 24), Day(11, 24), "Unpaid");

        var remoteOnly = await team.Manager.Client.GetJson("/api/calendar?year=2026&month=11&type=Remote");
        var unpaidOnly = await team.Manager.Client.GetJson("/api/calendar?year=2026&month=11&type=Unpaid");

        var remoteEvent = Assert.Single(remoteOnly.GetProperty("events").EnumerateArray());
        Assert.Equal("Remote", remoteEvent.Str("type"));
        Assert.Equal("Approved", remoteEvent.Str("status"));
        Assert.Equal("Unpaid", Assert.Single(unpaidOnly.GetProperty("events").EnumerateArray()).Str("type"));
    }

    [Fact]
    public async Task Approved_remote_days_cannot_be_edited()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        var id = (await employee.CreateRemoteIds(Day(11, 18)))[0];

        // Zilele auto-aprobate nu mai sunt „în așteptare”, deci nu se editează (se anulează).
        var edit = await employee.PutAsJsonAsync($"/api/remote-requests/{id}", new { days = new[] { Day(11, 19) } });
        await edit.ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
    }
}
