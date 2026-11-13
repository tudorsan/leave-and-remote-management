using System.Net;
using System.Net.Http.Json;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>Cerințele 7–10 (cereri de concediu) și regulile 2 și 9. „Azi” este vineri, 13.11.2026.</summary>
public class LeaveRequestTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    // Cerința 7
    [Theory]
    [InlineData("Vacation")]
    [InlineData("Medical")]
    [InlineData("Unpaid")]
    [InlineData("Special")]
    public async Task Employee_requests_each_leave_type_with_an_optional_comment(string type)
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        var response = await employee.Client.RequestLeave(Day(12, 7), Day(12, 8), type, "Plecare la munte");

        await response.ShouldHaveStatus(HttpStatusCode.Created);
        var body = await response.Body();
        Assert.Equal(type, body.Str("type"));
        Assert.Equal("Plecare la munte", body.Str("comment"));
    }

    [Fact]
    public async Task Unknown_leave_type_is_rejected()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        await (await employee.Client.RequestLeave(Day(12, 7), Day(12, 7), "Holiday")).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_LEAVE_TYPE");
    }

    // Cerința 8
    [Fact]
    public async Task Working_days_exclude_weekends_and_configured_public_holidays()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        // vineri 27.11 – miercuri 2.12: weekend 28–29, sărbători 30.11 și 1.12 => 27.11 și 2.12
        var response = await employee.Client.RequestLeave(Day(11, 27), Day(12, 2));

        await response.ShouldHaveStatus(HttpStatusCode.Created);
        Assert.Equal(2, (await response.Body()).Int("workingDays"));
    }

    [Fact]
    public async Task An_interval_without_working_days_is_rejected()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        await (await employee.Client.RequestLeave(Day(11, 28), Day(11, 29), "Unpaid")).ShouldFailWith(HttpStatusCode.BadRequest, "NO_WORKING_DAYS");
    }

    [Fact]
    public async Task Leave_across_new_year_uses_next_years_holidays()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        var response = await employee.Client.RequestLeave(Day(12, 28), Day(1, 8, 2027));

        await response.ShouldHaveStatus(HttpStatusCode.Created);
        Assert.Equal(7, (await response.Body()).Int("workingDays"));
    }

    // Cerința 9
    [Fact]
    public async Task A_new_request_is_pending_and_appears_in_the_calendar_immediately()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];

        var id = await employee.Client.CreateLeaveId(Day(11, 18), Day(11, 19));

        var calendar = await team.Manager.Client.GetJson("/api/calendar?year=2026&month=11");
        var events = calendar.GetProperty("events").EnumerateArray().Where(e => e.Int("requestId") == id).ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal("Pending", e.Str("status"));
            Assert.Equal("Vacation", e.Str("type"));
            Assert.Equal(employee.Id, e.Int("userId"));
        });
    }

    // Regula 2
    [Fact]
    public async Task Leave_cannot_start_in_the_past()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        await (await employee.Client.RequestLeave(Day(11, 12), Day(11, 16), "Medical")).ShouldFailWith(HttpStatusCode.BadRequest, "LEAVE_DATE_OUT_OF_RANGE");
    }

    [Fact]
    public async Task End_date_cannot_be_before_start_date()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        await (await employee.Client.RequestLeave(Day(12, 10), Day(12, 9))).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_DATE_RANGE");
    }

    // Setarea leave.min_days_ahead = 3 se aplică doar concediului de odihnă.
    [Fact]
    public async Task Vacation_needs_three_days_notice_but_other_types_may_start_today()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        await (await employee.Client.RequestLeave(Day(11, 15), Day(11, 15))).ShouldFailWith(HttpStatusCode.BadRequest, "LEAVE_DATE_OUT_OF_RANGE");
        await (await employee.Client.RequestLeave(Day(11, 16), Day(11, 16))).ShouldHaveStatus(HttpStatusCode.Created);
        await (await employee.Client.RequestLeave(Day(11, 13), Day(11, 13), "Medical")).ShouldHaveStatus(HttpStatusCode.Created);
    }

    // Setarea leave.max_days_ahead = 365
    [Fact]
    public async Task Leave_cannot_end_beyond_the_configured_horizon()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        await (await employee.Client.RequestLeave(Day(11, 12, 2027), Day(11, 12, 2027), "Unpaid")).ShouldHaveStatus(HttpStatusCode.Created);
        await (await employee.Client.RequestLeave(Day(11, 15, 2027), Day(11, 15, 2027), "Unpaid")).ShouldFailWith(HttpStatusCode.BadRequest, "LEAVE_DATE_OUT_OF_RANGE");
    }

    [Fact]
    public async Task Only_pending_requests_can_be_edited_and_edits_are_revalidated()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var id = await employee.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");

        var edit = await employee.PutAsJsonAsync($"/api/leave-requests/{id}", new { startDate = Day(12, 7), endDate = Day(12, 9), type = "Unpaid" });
        await edit.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.Equal(3, (await edit.Body()).Int("workingDays"));

        var invalid = await employee.PutAsJsonAsync($"/api/leave-requests/{id}", new { startDate = Day(12, 12), endDate = Day(12, 13), type = "Unpaid" });
        await invalid.ShouldFailWith(HttpStatusCode.BadRequest, "NO_WORKING_DAYS");

        await (await team.Manager.Client.ApproveLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);
        var afterApproval = await employee.PutAsJsonAsync($"/api/leave-requests/{id}", new { startDate = Day(12, 10), endDate = Day(12, 10), type = "Unpaid" });
        await afterApproval.ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
    }

    // Cerința 10
    [Fact]
    public async Task A_pending_request_can_always_be_cancelled()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        var id = await employee.CreateLeaveId(Day(11, 13), Day(11, 13), "Medical");

        var response = await employee.CancelLeave(id);

        await response.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.Equal("Canceled", (await response.Body()).Str("status"));
    }

    [Fact]
    public async Task An_approved_request_can_be_cancelled_only_before_it_starts()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var notStarted = await employee.CreateLeaveId(Day(12, 7), Day(12, 8), "Unpaid");
        var started = await employee.CreateLeaveId(Day(11, 16), Day(11, 18), "Unpaid");
        await (await team.Manager.Client.ApproveLeave(notStarted)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.ApproveLeave(started)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await employee.CancelLeave(notStarted)).ShouldHaveStatus(HttpStatusCode.OK);

        factory.SetToday(new DateOnly(2026, 11, 16));
        try
        {
            await (await employee.CancelLeave(started)).ShouldFailWith(HttpStatusCode.Conflict, "LEAVE_ALREADY_STARTED");
        }
        finally
        {
            factory.ResetToday();
        }
    }

    [Fact]
    public async Task Own_requests_show_status_rejection_reason_and_working_days()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var id = await employee.CreateLeaveId(Day(12, 7), Day(12, 9), "Special");
        await (await team.Manager.Client.RejectLeave(id, "Avem lansarea produsului")).ShouldHaveStatus(HttpStatusCode.OK);

        var own = (await employee.GetJson("/api/leave-requests")).EnumerateArray().Single(r => r.Int("id") == id);

        Assert.Equal("Rejected", own.Str("status"));
        Assert.Equal("Avem lansarea produsului", own.Str("rejectionReason"));
        Assert.Equal(3, own.Int("workingDays"));
        Assert.False(own.GetProperty("canCancel").GetBoolean());
    }
}

