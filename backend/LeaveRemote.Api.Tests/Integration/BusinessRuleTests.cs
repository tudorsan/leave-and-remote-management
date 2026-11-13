using System.Net;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>
/// „Reguli de business și validări” 1, 3, 6, 7, 8 și fluxul de aprobare (cerințele 14–15),
/// cu tranzițiile din diagrama de stări. Setări implicite; „azi” este vineri, 13.11.2026.
/// </summary>
public class BusinessRuleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    // Regula 1: o zi nu poate avea două cereri active, indiferent de tip.
    [Fact]
    public async Task Two_active_leave_requests_cannot_overlap()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        await employee.CreateLeaveId(Day(12, 7), Day(12, 9), "Unpaid");

        await (await employee.RequestLeave(Day(12, 9), Day(12, 10), "Special")).ShouldFailWith(HttpStatusCode.Conflict, "LEAVE_REQUEST_OVERLAP");
    }

    [Fact]
    public async Task Remote_and_leave_cannot_share_a_day_in_either_order()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        await employee.CreateRemoteIds(Day(11, 18));
        await employee.CreateLeaveId(Day(11, 23), Day(11, 23), "Unpaid");

        await (await employee.RequestLeave(Day(11, 17), Day(11, 19), "Unpaid")).ShouldFailWith(HttpStatusCode.Conflict, "LEAVE_REMOTE_OVERLAP");
        await (await employee.RequestRemote(Day(11, 23))).ShouldFailWith(HttpStatusCode.Conflict, "REMOTE_LEAVE_OVERLAP");
    }

    [Fact]
    public async Task Cancelled_and_rejected_requests_no_longer_block_the_day()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var cancelled = await employee.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var rejected = await employee.CreateLeaveId(Day(12, 8), Day(12, 8), "Unpaid");
        await (await employee.CancelLeave(cancelled)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.RejectLeave(rejected, "Suprapunere cu auditul")).ShouldHaveStatus(HttpStatusCode.OK);

        await (await employee.RequestLeave(Day(12, 7), Day(12, 8), "Unpaid")).ShouldHaveStatus(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Concurrent_requests_for_the_same_day_create_only_one()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => employee.RequestLeave(Day(12, 7), Day(12, 7), "Unpaid")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    // Regula 3: odihna nu poate depăși soldul disponibil (sold − aprobate − în așteptare).
    [Fact]
    public async Task Vacation_cannot_exceed_the_available_balance_including_pending_days()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        await employee.CreateLeaveId(Day(1, 4, 2027), Day(1, 22, 2027));   // 13 zile lucrătoare (6–7 ian sărbători)
        await employee.CreateLeaveId(Day(2, 1, 2027), Day(2, 5, 2027));    // 5 zile => 18 din 21 în 2027

        var tooMuch = await employee.RequestLeave(Day(2, 8, 2027), Day(2, 11, 2027)); // 4 zile > 3 disponibile
        await tooMuch.ShouldFailWith(HttpStatusCode.Conflict, "INSUFFICIENT_LEAVE_BALANCE");
        await (await employee.RequestLeave(Day(2, 8, 2027), Day(2, 10, 2027))).ShouldHaveStatus(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Concurrent_vacation_requests_cannot_overspend_the_balance()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        var mondays = Enumerable.Range(0, 5).Select(i => new DateOnly(2027, 3, 1).AddDays(7 * i)); // 5 × 5 zile = 25 > 21

        var responses = await Task.WhenAll(mondays.Select(monday =>
            employee.RequestLeave(monday.ToString("yyyy-MM-dd"), monday.AddDays(4).ToString("yyyy-MM-dd"))));

        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var balance = await employee.Balance(year: 2027);
        Assert.Equal(20, balance.Int("pendingDays"));
        Assert.Equal(1, balance.Int("availableDays"));
    }

    [Fact]
    public async Task Only_vacation_consumes_the_balance()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        await employee.CreateLeaveId(Day(12, 7), Day(12, 11), "Medical");
        await employee.CreateLeaveId(Day(12, 14), Day(12, 18), "Unpaid");

        var balance = await employee.Balance();

        Assert.Equal(0, balance.Int("pendingDays"));
        Assert.Equal(21, balance.Int("availableDays"));
    }

    // Regula 6: procentul maxim din echipă absent în aceeași zi (implicit 50%), doar concediile aprobate.
    [Fact]
    public async Task Team_absence_limit_counts_only_approved_leave()
    {
        var team = await _api.CreateTeamAsync(employees: 2); // 3 membri: 1 absent = 33%, 2 = 67%
        var (first, second) = (team.Employees[0].Client, team.Employees[1].Client);

        var pending = await first.CreateLeaveId(Day(12, 7), Day(12, 7));
        await (await second.RequestLeave(Day(12, 7), Day(12, 7))).ShouldHaveStatus(HttpStatusCode.Created);

        await (await team.Manager.Client.ApproveLeave(pending)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await second.RequestLeave(Day(12, 8), Day(12, 8))).ShouldHaveStatus(HttpStatusCode.Created);
        await (await team.Manager.Client.RequestLeave(Day(12, 7), Day(12, 7), "Unpaid")).ShouldFailWith(HttpStatusCode.Conflict, "TEAM_ABSENCE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Team_absence_limit_is_checked_again_on_approval()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var first = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 9));
        var second = await team.Employees[1].Client.CreateLeaveId(Day(12, 9), Day(12, 10));

        await (await team.Manager.Client.ApproveLeave(first)).ShouldHaveStatus(HttpStatusCode.OK);
        var response = await team.Manager.Client.ApproveLeave(second);

        await response.ShouldFailWith(HttpStatusCode.Conflict, "TEAM_ABSENCE_LIMIT_EXCEEDED");
        Assert.Contains("2026-12-09", (await response.Body()).Str("message"));
    }

    [Fact]
    public async Task Medical_leave_is_never_blocked_by_the_team_limit_but_counts_as_absence()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var vacation = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7));
        await (await team.Manager.Client.ApproveLeave(vacation)).ShouldHaveStatus(HttpStatusCode.OK);

        var medical = await team.Employees[1].Client.CreateLeaveId(Day(12, 7), Day(12, 8), "Medical");
        await (await team.Manager.Client.ApproveLeave(medical)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await team.Manager.Client.RequestLeave(Day(12, 8), Day(12, 8), "Unpaid")).ShouldFailWith(HttpStatusCode.Conflict, "TEAM_ABSENCE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Remote_work_does_not_count_as_absence()
    {
        var team = await _api.CreateTeamAsync(employees: 1); // 2 membri: 1 absent = 50% (permis)
        await team.Employees[0].Client.CreateRemoteIds(Day(11, 18));

        await (await team.Manager.Client.RequestLeave(Day(11, 18), Day(11, 18))).ShouldHaveStatus(HttpStatusCode.Created);
    }

    // Regula 7
    [Fact]
    public async Task Manager_cannot_approve_own_request_and_admin_cannot_either()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var admin = await _api.AdminAsync();
        var managerRequest = await team.Manager.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var adminRequest = await admin.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");

        await (await team.Manager.Client.ApproveLeave(managerRequest)).ShouldFailWith(HttpStatusCode.Conflict, "SELF_APPROVAL_FORBIDDEN");
        await (await admin.Client.ApproveLeave(adminRequest)).ShouldFailWith(HttpStatusCode.Conflict, "SELF_APPROVAL_FORBIDDEN");
        await (await admin.Client.RejectLeave(adminRequest, "Motiv suficient de lung")).ShouldFailWith(HttpStatusCode.Conflict, "SELF_REJECTION_FORBIDDEN");
    }

    // Regula 8 + cerința 14
    [Theory]
    [InlineData("Scurt")]
    [InlineData("123456789")]
    [InlineData("             ")]
    public async Task Rejection_requires_a_reason_of_at_least_ten_characters(string reason)
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var id = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        await (await team.Manager.Client.RejectLeave(id, reason)).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_REJECTION_REASON");
        await (await team.Manager.Client.RejectLeave(id, "Inventar anual")).ShouldHaveStatus(HttpStatusCode.OK);
    }

    // Cerința 15 + diagrama de stări
    [Fact]
    public async Task Requester_sees_the_new_status_and_the_rejection_reason()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var approved = await employee.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var rejected = await employee.CreateLeaveId(Day(12, 8), Day(12, 8), "Unpaid");

        await (await team.Manager.Client.ApproveLeave(approved)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.RejectLeave(rejected, "Inventar anual in acea zi")).ShouldHaveStatus(HttpStatusCode.OK);

        var own = (await employee.GetJson("/api/leave-requests")).EnumerateArray().ToDictionary(r => r.Int("id"));
        Assert.Equal("Approved", own[approved].Str("status"));
        Assert.Equal("Rejected", own[rejected].Str("status"));
        Assert.Equal("Inventar anual in acea zi", own[rejected].Str("rejectionReason"));
    }

    [Fact]
    public async Task Only_pending_requests_can_be_approved_or_rejected()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var manager = team.Manager.Client;
        var approved = await employee.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var rejected = await employee.CreateLeaveId(Day(12, 8), Day(12, 8), "Unpaid");
        var cancelled = await employee.CreateLeaveId(Day(12, 9), Day(12, 9), "Unpaid");
        await (await manager.ApproveLeave(approved)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await manager.RejectLeave(rejected, "Motiv suficient de lung")).ShouldHaveStatus(HttpStatusCode.OK);
        await (await employee.CancelLeave(cancelled)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await manager.ApproveLeave(approved)).ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
        await (await manager.RejectLeave(approved, "Motiv suficient de lung")).ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
        await (await manager.ApproveLeave(rejected)).ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
        await (await manager.ApproveLeave(cancelled)).ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
        await (await employee.CancelLeave(rejected)).ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
        await (await employee.CancelLeave(cancelled)).ShouldFailWith(HttpStatusCode.Conflict, "INVALID_REQUEST_STATUS");
    }

    [Fact]
    public async Task Approving_consumes_the_balance_and_cancelling_restores_it()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];
        var id = await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 11));

        var pending = await employee.Client.Balance();
        await (await team.Manager.Client.ApproveLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);
        var approved = await employee.Client.Balance();
        await (await employee.Client.CancelLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);
        var cancelled = await employee.Client.Balance();

        Assert.Equal((0, 5, 16), (pending.Int("usedDays"), pending.Int("pendingDays"), pending.Int("availableDays")));
        Assert.Equal((5, 0, 16), (approved.Int("usedDays"), approved.Int("pendingDays"), approved.Int("availableDays")));
        Assert.Equal((0, 0, 21), (cancelled.Int("usedDays"), cancelled.Int("pendingDays"), cancelled.Int("availableDays")));
    }
}
