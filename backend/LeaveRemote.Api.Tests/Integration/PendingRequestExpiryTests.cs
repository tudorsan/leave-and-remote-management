using System.Net;
using LeaveRemote.Api.Services;
using LeaveRemote.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>
/// Cererile rămase „În așteptare” după ultima zi a perioadei sunt anulate automat, cu motiv,
/// soldul rezervat se eliberează, iar solicitantul este anunțat prin email.
/// </summary>
public class PendingRequestExpiryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    /// <summary>Mută „azi” la data dată și rulează expirarea, cum ar face aplicația în fundal.</summary>
    private async Task ExpireOnAsync(DateOnly today)
    {
        factory.SetToday(today);
        try
        {
            using var scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PendingRequestExpiryService>().ExpireAsync();
        }
        finally
        {
            factory.ResetToday();
        }
    }

    [Fact]
    public async Task A_pending_request_is_cancelled_after_its_last_day_and_the_balance_is_released()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];
        var id = await employee.Client.CreateLeaveId(Day(11, 16), Day(11, 18));
        Assert.Equal(3, (await employee.Client.Balance()).Int("pendingDays"));

        await ExpireOnAsync(new DateOnly(2026, 11, 19));

        var own = (await employee.Client.GetJson("/api/leave-requests")).EnumerateArray().Single(r => r.Int("id") == id);
        Assert.Equal("Canceled", own.Str("status"));
        Assert.Equal(PendingRequestExpiryService.ExpiredReason, own.Str("cancellationReason"));
        var balance = await employee.Client.Balance();
        Assert.Equal(0, balance.Int("pendingDays"));
        Assert.Equal(21, balance.Int("availableDays"));
    }

    [Fact]
    public async Task A_pending_request_whose_period_is_still_running_can_still_be_decided()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var id = await team.Employees[0].Client.CreateLeaveId(Day(11, 13), Day(11, 18), "Medical");

        await ExpireOnAsync(new DateOnly(2026, 11, 18)); // ultima zi: perioada încă nu s-a încheiat

        var own = (await team.Employees[0].Client.GetJson("/api/leave-requests")).EnumerateArray().Single(r => r.Int("id") == id);
        Assert.Equal("Pending", own.Str("status"));
        await (await team.Manager.Client.ApproveLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Approved_and_rejected_requests_are_not_touched()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var approved = await employee.CreateLeaveId(Day(11, 16), Day(11, 16), "Unpaid");
        var rejected = await employee.CreateLeaveId(Day(11, 17), Day(11, 17), "Unpaid");
        await (await team.Manager.Client.ApproveLeave(approved)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.RejectLeave(rejected, "Inventar anual")).ShouldHaveStatus(HttpStatusCode.OK);

        await ExpireOnAsync(new DateOnly(2026, 11, 30));

        var own = (await employee.GetJson("/api/leave-requests")).EnumerateArray().ToDictionary(r => r.Int("id"), r => r.Str("status"));
        Assert.Equal("Approved", own[approved]);
        Assert.Equal("Rejected", own[rejected]);
    }

    [Fact]
    public async Task Pending_remote_days_expire_too_and_the_requester_gets_an_email()
    {
        await _api.UpdateSettingsAsync(remoteRequiresApproval: true);
        try
        {
            var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];
            var past = (await employee.Client.CreateRemoteIds(Day(11, 17)))[0];
            var future = (await employee.Client.CreateRemoteIds(Day(11, 24)))[0];

            await ExpireOnAsync(new DateOnly(2026, 11, 20));

            var own = (await employee.Client.GetJson("/api/remote-requests")).EnumerateArray().ToDictionary(r => r.Int("id"), r => r.Str("status"));
            Assert.Equal("Canceled", own[past]);
            Assert.Equal("Pending", own[future]);
            var email = factory.Emails.SentTo(employee.Email).Last();
            Assert.Equal("Cererea ta de lucru remote a fost anulată automat", email.Subject);
            Assert.Contains("17.11.2026", email.Body);
        }
        finally
        {
            await _api.UpdateSettingsAsync();
        }
    }

    [Fact]
    public async Task The_requester_is_told_that_the_leave_request_was_cancelled_automatically()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];
        await employee.Client.CreateLeaveId(Day(11, 16), Day(11, 17));

        await ExpireOnAsync(new DateOnly(2026, 11, 18));

        var email = factory.Emails.SentTo(employee.Email).Last();
        Assert.Equal("Cererea ta de concediu de odihnă a fost anulată automat", email.Subject);
        Assert.Contains("16.11.2026 – 17.11.2026", email.Body);
        Assert.Contains("au fost eliberate", email.Body);
    }
}
