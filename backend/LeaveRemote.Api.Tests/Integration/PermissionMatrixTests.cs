using System.Net;
using System.Net.Http.Json;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>
/// Matricea „Roluri și permisiuni” din PDF, rând cu rând. Permisiunile sunt verificate în backend:
/// fiecare acțiune interzisă întoarce 403 chiar dacă interfața nu o oferă.
/// </summary>
public class PermissionMatrixTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    // „Vede calendarul echipei sale”: da / da / da
    [Fact]
    public async Task Everyone_sees_the_calendar_of_their_own_team()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        await team.Employees[0].Client.CreateLeaveId(Day(11, 18), Day(11, 18), "Medical");

        foreach (var member in new[] { team.Employees[0], team.Manager })
        {
            var calendar = await member.Client.GetJson("/api/calendar?year=2026&month=11");
            Assert.Equal(team.TeamId, calendar.Int("teamId"));
            Assert.Equal(2, calendar.GetProperty("members").GetArrayLength());
            Assert.Single(calendar.GetProperty("events").EnumerateArray());
        }
    }

    // „Vede calendarul tuturor echipelor”: nu / nu / da
    [Fact]
    public async Task Only_admin_sees_other_teams_calendars()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var otherTeam = await _api.CreateTeamAsync(employees: 0);
        var admin = await _api.AdminAsync();

        await (await team.Employees[0].Client.GetAsync($"/api/calendar?teamId={otherTeam.TeamId}")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await team.Manager.Client.GetAsync($"/api/calendar?teamId={otherTeam.TeamId}")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
        var calendar = await admin.Client.GetJson($"/api/calendar?teamId={otherTeam.TeamId}");
        Assert.Equal(otherTeam.TeamId, calendar.Int("teamId"));
    }

    // „Creează / editează / anulează propriile cereri”: da / da / da
    [Fact]
    public async Task Every_role_manages_own_requests()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var admin = await _api.AdminAsync();

        foreach (var client in new[] { team.Employees[0].Client, team.Manager.Client, admin.Client })
        {
            var id = await client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
            var edit = await client.PutAsJsonAsync($"/api/leave-requests/{id}",
                new { startDate = Day(12, 8), endDate = Day(12, 8), type = "Unpaid" });
            await edit.ShouldHaveStatus(HttpStatusCode.OK);
            await (await client.CancelLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Nobody_can_edit_or_cancel_someone_elses_request()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var id = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var other = team.Employees[1].Client;

        await (await other.CancelLeave(id)).ShouldFailWith(HttpStatusCode.NotFound, "LEAVE_REQUEST_NOT_FOUND");
        var edit = await other.PutAsJsonAsync($"/api/leave-requests/{id}", new { startDate = Day(12, 8), endDate = Day(12, 8), type = "Unpaid" });
        await edit.ShouldFailWith(HttpStatusCode.NotFound, "LEAVE_REQUEST_NOT_FOUND");
    }

    // „Aprobă / respinge cereri din echipa sa”: nu / da / nu(dar Adminul aprobă orice)
    [Fact]
    public async Task Manager_reviews_only_own_team_and_employees_cannot_review()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var otherTeam = await _api.CreateTeamAsync(employees: 0);
        var id = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");

        await (await team.Employees[1].Client.ApproveLeave(id)).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await otherTeam.Manager.Client.ApproveLeave(id)).ShouldFailWith(HttpStatusCode.Forbidden, "NOT_YOUR_TEAM");
        await (await team.Manager.Client.ApproveLeave(id)).ShouldHaveStatus(HttpStatusCode.OK);
    }

    // „Aprobă / respinge orice cerere”: nu / nu / da
    [Fact]
    public async Task Admin_reviews_any_request()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var admin = await _api.AdminAsync();
        var first = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var second = await team.Employees[0].Client.CreateLeaveId(Day(12, 8), Day(12, 8), "Unpaid");

        await (await admin.Client.ApproveLeave(first)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await admin.Client.RejectLeave(second, "Nu se poate in acea zi")).ShouldHaveStatus(HttpStatusCode.OK);
    }

    // Un Manager are un manager propriu; cererile lui sunt aprobate de managerul său sau de Admin.
    [Fact]
    public async Task A_managers_request_is_reviewed_by_their_own_manager_or_admin()
    {
        var team = await _api.CreateTeamAsync(employees: 0);
        var boss = await _api.CreateUserAsync("Manager", await _api.CreateTeamAsync(), (await _api.AdminAsync()).Id);
        var subordinateManager = await _api.CreateUserAsync("Manager", team.TeamId, boss.Id);
        var first = await subordinateManager.Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var second = await subordinateManager.Client.CreateLeaveId(Day(12, 8), Day(12, 8), "Unpaid");

        await (await team.Manager.Client.ApproveLeave(first)).ShouldFailWith(HttpStatusCode.Forbidden, "NOT_DIRECT_MANAGER");
        var pending = await boss.Client.GetJson("/api/leave-requests/pending");
        Assert.Contains(pending.EnumerateArray(), r => r.Int("id") == first);
        await (await boss.Client.ApproveLeave(first)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await (await _api.AdminAsync()).Client.ApproveLeave(second)).ShouldHaveStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pending_list_contains_only_requests_the_reviewer_may_decide()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var otherTeam = await _api.CreateTeamAsync(employees: 1);
        var mine = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var foreign = await otherTeam.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7), "Unpaid");
        var own = await team.Manager.Client.CreateLeaveId(Day(12, 9), Day(12, 9), "Unpaid");

        var ids = (await team.Manager.Client.GetJson("/api/leave-requests/pending")).EnumerateArray().Select(r => r.Int("id")).ToList();

        Assert.Contains(mine, ids);
        Assert.DoesNotContain(foreign, ids);
        Assert.DoesNotContain(own, ids);
        await (await team.Employees[0].Client.GetAsync("/api/leave-requests/pending")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    // „Vede soldul de concediu al altora”: nu / echipa sa / toți
    [Fact]
    public async Task Balance_visibility_follows_the_matrix()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var otherTeam = await _api.CreateTeamAsync(employees: 1);
        var admin = await _api.AdminAsync();
        var colleague = team.Employees[1].Id;

        await (await team.Employees[0].Client.GetAsync($"/api/leave-balances/{colleague}")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
        await team.Manager.Client.Balance(colleague);
        await (await team.Manager.Client.GetAsync($"/api/leave-balances/{otherTeam.Employees[0].Id}")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
        await admin.Client.Balance(otherTeam.Employees[0].Id);
    }

    // „Administrează utilizatori, echipe, roluri”, „Modifică setările globale”, „Setează soldul anual”: doar Admin
    [Fact]
    public async Task Only_admin_administers_users_teams_settings_and_allocations()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var target = team.Employees[0].Id;

        foreach (var client in new[] { team.Employees[0].Client, team.Manager.Client })
        {
            await (await client.PostAsJsonAsync("/api/users", new { firstName = "X", lastName = "Y", email = "x@y.ro", password = "Parola123!", role = "Admin" }))
                .ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
            await (await client.PutAsJsonAsync($"/api/users/{target}", new { firstName = "X", lastName = "Y", email = "x@y.ro", role = "Admin", teamId = team.TeamId }))
                .ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
            await (await client.Patch($"/api/users/{target}/deactivate")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
            await (await client.PostAsJsonAsync("/api/teams", new { name = "Noua" })).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
            await (await client.GetAsync("/api/settings")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
            await (await client.PutAsJsonAsync("/api/settings/holidays", Array.Empty<object>())).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
            await (await client.PutAsJsonAsync($"/api/leave-balances/{target}/2026", new { totalDays = 40 })).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
        }
        await (await team.Employees[0].Client.GetAsync("/api/users")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Role_changes_take_effect_immediately_even_for_existing_tokens()
    {
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 0);
        var promoted = await _api.CreateUserAsync("Employee", team.TeamId, team.Manager.Id);
        await (await promoted.Client.GetAsync("/api/settings")).ShouldFailWith(HttpStatusCode.Forbidden, "FORBIDDEN");

        var update = await admin.Client.PutAsJsonAsync($"/api/users/{promoted.Id}",
            new { firstName = "Employee", lastName = "Test", email = promoted.Email, role = "Admin", teamId = team.TeamId, managerId = admin.Id });
        await update.ShouldHaveStatus(HttpStatusCode.OK);

        await (await promoted.Client.GetAsync("/api/settings")).ShouldHaveStatus(HttpStatusCode.OK);
    }
}
