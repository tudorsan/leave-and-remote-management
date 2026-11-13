using System.Net;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>
/// Cerința 16 (opțională): notificare prin email la schimbarea stării, simulată în log.
/// În teste, „poștașul” este înlocuit cu <see cref="FakeEmailSender"/>, care păstrează emailurile.
/// </summary>
public class NotificationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);
    private FakeEmailSender Emails => factory.Emails;

    [Fact]
    public async Task A_new_leave_request_notifies_the_team_manager_only()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var (employee, colleague) = (team.Employees[0], team.Employees[1]);

        await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 9));

        var email = Assert.Single(Emails.SentTo(team.Manager.Email));
        Assert.Contains("Cerere nouă de aprobat", email.Subject);
        Assert.Contains("concediu de odihnă", email.Body);
        Assert.Contains("07.12.2026 – 09.12.2026", email.Body);
        Assert.Contains("3 zile lucrătoare", email.Body);
        Assert.Empty(Emails.SentTo(employee.Email));
        Assert.Empty(Emails.SentTo(colleague.Email));
    }

    [Fact]
    public async Task Approval_notifies_the_requester()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];
        var id = await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 9), "Unpaid");

        await (await team.Manager.Client.ApproveLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);

        var email = Assert.Single(Emails.SentTo(employee.Email));
        Assert.Equal("Cererea ta de concediu fără plată a fost aprobată", email.Subject);
        Assert.Contains("07.12.2026 – 09.12.2026", email.Body);
        Assert.Contains("Manager Test", email.Body);
    }

    [Fact]
    public async Task Rejection_notifies_the_requester_with_the_reason()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];
        var id = await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Special");

        await (await team.Manager.Client.RejectLeave(id, "Avem lansarea produsului")).ShouldHaveStatus(HttpStatusCode.OK);

        var email = Assert.Single(Emails.SentTo(employee.Email));
        Assert.Contains("a fost respinsă", email.Subject);
        Assert.Contains("Motiv: Avem lansarea produsului", email.Body);
    }

    [Fact]
    public async Task Auto_approved_remote_days_send_one_confirmation_to_the_requester()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];

        await employee.Client.CreateRemoteIds(Day(11, 17), Day(11, 19));

        var email = Assert.Single(Emails.SentTo(employee.Email));
        Assert.Contains("aprobate", email.Subject);
        Assert.Contains("17.11.2026, 19.11.2026", email.Body);
        Assert.Empty(Emails.SentTo(team.Manager.Email));
    }

    [Fact]
    public async Task Remote_days_needing_approval_notify_the_manager_and_then_the_requester()
    {
        await _api.UpdateSettingsAsync(remoteRequiresApproval: true);
        try
        {
            var team = await _api.CreateTeamAsync(employees: 1);
            var employee = team.Employees[0];
            var ids = await employee.Client.CreateRemoteIds(Day(11, 17), Day(11, 18));

            Assert.Contains("Zile remote de aprobat", Assert.Single(Emails.SentTo(team.Manager.Email)).Subject);
            Assert.Empty(Emails.SentTo(employee.Email));

            await (await team.Manager.Client.ApproveRemote(ids[0])).ShouldHaveStatus(HttpStatusCode.OK);
            await (await team.Manager.Client.RejectRemote(ids[1], "Sedinta de planificare")).ShouldHaveStatus(HttpStatusCode.OK);

            var emails = Emails.SentTo(employee.Email);
            Assert.Equal(2, emails.Count);
            Assert.Contains(emails, e => e.Subject.Contains("aprobată") && e.Body.Contains("17.11.2026"));
            Assert.Contains(emails, e => e.Subject.Contains("respinsă") && e.Body.Contains("Motiv: Sedinta de planificare"));
        }
        finally
        {
            await _api.UpdateSettingsAsync();
        }
    }

    [Fact]
    public async Task Cancelling_an_approved_request_notifies_the_manager()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];
        var id = await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        await (await team.Manager.Client.ApproveLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await employee.Client.CancelLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);

        var email = Emails.SentTo(team.Manager.Email).Last();
        Assert.Contains("Cerere anulată", email.Subject);
        Assert.Contains("cererea aprobată", email.Body);
    }

    [Fact]
    public async Task A_managers_request_notifies_their_direct_superior_not_their_team()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var boss = await _api.CreateUserAsync("Manager", await _api.CreateTeamAsync(), (await _api.AdminAsync()).Id);
        var manager = await _api.CreateUserAsync("Manager", team.TeamId, boss.Id);

        await manager.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");

        Assert.Single(Emails.SentTo(boss.Email));
        Assert.Empty(Emails.SentTo(team.Manager.Email));
    }

    [Fact]
    public async Task Without_a_team_manager_the_admins_are_notified()
    {
        var admin = await _api.AdminAsync();
        var lonelyEmployee = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);
        var before = Emails.SentTo(admin.Email).Count;

        await lonelyEmployee.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Medical");

        Assert.Equal(before + 1, Emails.SentTo(admin.Email).Count);
    }

    [Fact]
    public async Task No_email_is_sent_when_the_approval_fails()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var first = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7));
        var second = await team.Employees[1].Client.CreateLeaveId(Day(12, 7), Day(12, 7));
        await (await team.Manager.Client.ApproveLeave(first)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await team.Manager.Client.ApproveLeave(second)).ShouldFailWith(HttpStatusCode.Conflict, "TEAM_ABSENCE_LIMIT_EXCEEDED");

        Assert.Empty(Emails.SentTo(team.Employees[1].Email));
    }

    [Fact]
    public async Task No_email_is_sent_when_the_rejection_is_invalid()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];
        var id = await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");

        await (await team.Manager.Client.RejectLeave(id, "Scurt")).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_REJECTION_REASON");

        Assert.Empty(Emails.SentTo(employee.Email));
    }

    [Fact]
    public async Task No_email_is_sent_when_the_request_itself_is_invalid()
    {
        var team = await _api.CreateTeamAsync(employees: 1);

        await (await team.Employees[0].Client.RequestLeave(Day(11, 28), Day(11, 29), "Unpaid")).ShouldFailWith(HttpStatusCode.BadRequest, "NO_WORKING_DAYS");

        Assert.Empty(Emails.SentTo(team.Manager.Email));
    }
}
