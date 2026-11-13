using System.Net;
using System.Net.Http.Json;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>Cerința 2 (administrarea utilizatorilor) și cerințele 17–18 (soldul anual per angajat).</summary>
public class AdministrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    // Cerința 2
    [Fact]
    public async Task Admin_creates_a_user_with_role_team_and_manager()
    {
        var team = await _api.CreateTeamAsync(employees: 0);
        var admin = await _api.AdminAsync();

        var response = await admin.Client.PostAsJsonAsync("/api/users", new
        {
            firstName = "Ana", lastName = "Pop", email = "Ana.Pop@Test.ro", password = "Parola123!",
            role = "Employee", teamId = team.TeamId, managerId = team.Manager.Id
        });

        await response.ShouldHaveStatus(HttpStatusCode.Created);
        var user = await response.Body();
        Assert.Equal("ana.pop@test.ro", user.Str("email"));
        Assert.Equal("Employee", user.Str("role"));
        Assert.Equal(team.TeamId, user.Int("teamId"));
        Assert.Equal(team.Manager.Id, user.Int("managerId"));
    }

    [Fact]
    public async Task User_data_is_validated()
    {
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 1);
        object User(string email, string password, string role, int? teamId, int? managerId = null) =>
            new { firstName = "A", lastName = "B", email, password, role, teamId, managerId };

        await (await admin.Client.PostAsJsonAsync("/api/users", User("fara-arond", "Parola123!", "Employee", team.TeamId))).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_EMAIL");
        await (await admin.Client.PostAsJsonAsync("/api/users", User("scurta@test.ro", "123", "Employee", team.TeamId))).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_PASSWORD");
        await (await admin.Client.PostAsJsonAsync("/api/users", User("rol@test.ro", "Parola123!", "Boss", team.TeamId))).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_ROLE");
        await (await admin.Client.PostAsJsonAsync("/api/users", User(team.Employees[0].Email, "Parola123!", "Employee", team.TeamId))).ShouldFailWith(HttpStatusCode.Conflict, "EMAIL_ALREADY_EXISTS");
        await (await admin.Client.PostAsJsonAsync("/api/users", User("fara-echipa@test.ro", "Parola123!", "Employee", null))).ShouldFailWith(HttpStatusCode.BadRequest, "EMPLOYEE_TEAM_REQUIRED");
        await (await admin.Client.PostAsJsonAsync("/api/users", User("manager@test.ro", "Parola123!", "Manager", team.TeamId))).ShouldFailWith(HttpStatusCode.BadRequest, "MANAGER_HIERARCHY_REQUIRED");
        await (await admin.Client.PostAsJsonAsync("/api/users", User("sef@test.ro", "Parola123!", "Employee", team.TeamId, team.Employees[0].Id))).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_MANAGER");
    }

    [Fact]
    public async Task An_employees_direct_manager_must_belong_to_the_same_team()
    {
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 0);
        var otherTeam = await _api.CreateTeamAsync(employees: 0);

        var response = await admin.Client.PostAsJsonAsync("/api/users", new
        {
            firstName = "A", lastName = "B", email = $"x-{Guid.NewGuid():N}@test.ro", password = "Parola123!",
            role = "Employee", teamId = team.TeamId, managerId = otherTeam.Manager.Id
        });

        await response.ShouldFailWith(HttpStatusCode.BadRequest, "MANAGER_TEAM_MISMATCH");
    }

    [Fact]
    public async Task Reporting_hierarchy_cannot_contain_cycles()
    {
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 0);
        var lower = await _api.CreateUserAsync("Manager", team.TeamId, team.Manager.Id);

        var response = await admin.Client.PutAsJsonAsync($"/api/users/{team.Manager.Id}", new
        {
            firstName = "Manager", lastName = "Test", email = team.Manager.Email, role = "Manager", teamId = team.TeamId, managerId = lower.Id
        });

        await response.ShouldFailWith(HttpStatusCode.Conflict, "MANAGER_HIERARCHY_CYCLE");
    }

    [Fact]
    public async Task Deactivation_keeps_the_user_cancels_pending_requests_and_can_be_undone()
    {
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0];
        var pending = await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 7));

        await (await admin.Client.Patch($"/api/users/{employee.Id}/deactivate")).ShouldHaveStatus(HttpStatusCode.OK);

        var users = (await admin.Client.GetJson("/api/users")).EnumerateArray().ToList();
        Assert.False(users.Single(u => u.Int("id") == employee.Id).GetProperty("isActive").GetBoolean());
        var calendar = await team.Manager.Client.GetJson("/api/calendar?year=2026&month=12");
        Assert.DoesNotContain(calendar.GetProperty("members").EnumerateArray(), m => m.Int("id") == employee.Id);
        Assert.DoesNotContain((await team.Manager.Client.GetJson("/api/leave-requests/pending")).EnumerateArray(), r => r.Int("id") == pending);

        await (await admin.Client.Patch($"/api/users/{employee.Id}/reactivate")).ShouldHaveStatus(HttpStatusCode.OK);
        var (_, client) = await _api.LoginAsync(employee.Email, TestApi.Password);
        var own = Assert.Single((await client.GetJson("/api/leave-requests")).EnumerateArray());
        Assert.Equal("Canceled", own.Str("status"));
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves_or_a_manager_with_active_subordinates()
    {
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 1);

        await (await admin.Client.Patch($"/api/users/{admin.Id}/deactivate")).ShouldFailWith(HttpStatusCode.BadRequest, "CANNOT_DEACTIVATE_SELF");
        await (await admin.Client.Patch($"/api/users/{team.Manager.Id}/deactivate")).ShouldFailWith(HttpStatusCode.Conflict, "MANAGER_HAS_SUBORDINATES");
    }

    [Fact]
    public async Task Teams_are_created_renamed_and_deactivated_only_when_empty()
    {
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 0);
        var empty = await _api.CreateTeamAsync();

        await (await admin.Client.PutAsJsonAsync($"/api/teams/{empty}", new { name = $"Redenumită {Guid.NewGuid():N}" })).ShouldHaveStatus(HttpStatusCode.OK);
        await (await admin.Client.Patch($"/api/teams/{team.TeamId}/deactivate")).ShouldFailWith(HttpStatusCode.Conflict, "TEAM_HAS_ACTIVE_MEMBERS");
        await (await admin.Client.Patch($"/api/teams/{empty}/deactivate")).ShouldHaveStatus(HttpStatusCode.OK);
    }

    // Cerința 17
    [Fact]
    public async Task Admin_sets_the_annual_maximum_per_employee_which_also_applies_to_following_years()
    {
        var admin = await _api.AdminAsync();
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        await (await admin.Client.PutAsJsonAsync($"/api/leave-balances/{employee.Id}/2026", new { totalDays = 25 })).ShouldHaveStatus(HttpStatusCode.OK);

        Assert.Equal(25, (await employee.Client.Balance()).Int("totalDays"));
        Assert.Equal(25, (await employee.Client.Balance(year: 2027)).Int("totalDays"));
    }

    [Fact]
    public async Task Allocation_cannot_drop_below_days_already_used_or_reserved()
    {
        var admin = await _api.AdminAsync();
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];
        await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 11));

        var response = await admin.Client.PutAsJsonAsync($"/api/leave-balances/{employee.Id}/2026", new { totalDays = 4 });

        await response.ShouldFailWith(HttpStatusCode.Conflict, "BALANCE_BELOW_RESERVED");
    }

    // Cerința 18
    [Fact]
    public async Task Employee_sees_remaining_used_and_pending_days()
    {
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var approved = await employee.CreateLeaveId(Day(11, 23), Day(11, 25));
        await employee.CreateLeaveId(Day(12, 14), Day(12, 15));
        await (await team.Manager.Client.ApproveLeave(approved)).ShouldHaveStatus(HttpStatusCode.OK);

        var balance = await employee.Balance();

        Assert.Equal(2026, balance.Int("year"));
        Assert.Equal(21, balance.Int("totalDays"));
        Assert.Equal(3, balance.Int("usedDays"));
        Assert.Equal(2, balance.Int("pendingDays"));
        Assert.Equal(16, balance.Int("availableDays"));
    }

    [Fact]
    public async Task Leave_across_the_new_year_consumes_each_years_balance_separately()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;

        await employee.CreateLeaveId(Day(12, 28), Day(1, 8, 2027));

        Assert.Equal(4, (await employee.Balance(year: 2026)).Int("pendingDays"));
        Assert.Equal(3, (await employee.Balance(year: 2027)).Int("pendingDays"));
    }
}
