using System.Net;
using System.Net.Http.Json;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>
/// „Setări configurabile”: valorile implicite, editarea de către Admin, efectul imediat asupra cererilor noi
/// și faptul că cererile existente nu se revalidează. Clasa are baza ei, deci schimbarea setărilor
/// nu afectează celelalte teste; fiecare test își fixează explicit setările de care depinde.
/// </summary>
public class SettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Database_is_seeded_with_the_default_settings_and_romanian_holidays()
    {
        var fresh = await (await _api.AdminAsync()).Client.GetJson("/api/settings");
        var keys = fresh.GetProperty("settings").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(8, keys.Count);
        Assert.Contains("leave.max_team_absent_pct", keys);
        var holidays = fresh.GetProperty("holidays").EnumerateArray().Select(h => h.Str("date")).ToList();

        // Sărbători mobile: Paștele ortodox 2026 = 12 aprilie, 2027 = 2 mai.
        foreach (var date in new[] { "2026-01-01", "2026-04-10", "2026-04-12", "2026-04-13", "2026-06-01", "2026-11-30", "2026-12-01", "2026-12-25", "2027-04-30", "2027-05-02", "2027-05-03", "2027-06-20", "2027-06-21" })
        {
            Assert.Contains(date, holidays);
        }
    }

    [Fact]
    public async Task Settings_are_validated_by_the_backend()
    {
        var admin = await _api.AdminAsync();

        var response = await admin.Client.PutAsJsonAsync("/api/settings", new
        {
            remoteMinDaysAhead = 10, remoteMaxDaysAhead = 5, remoteMaxDaysPerWeek = 2, remoteRequiresApproval = false,
            leaveMinDaysAhead = 3, leaveMaxDaysAhead = 365, leaveDefaultAnnualDays = 21, leaveMaxTeamAbsentPct = 150
        });

        await response.ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_SETTINGS");
    }

    // Cerința 13: setarea globală decide dacă remote-ul necesită aprobare.
    [Fact]
    public async Task When_remote_requires_approval_days_start_pending_and_the_manager_decides()
    {
        await _api.UpdateSettingsAsync(remoteRequiresApproval: true);
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;

        var response = await employee.RequestRemote(Day(11, 17), Day(11, 18));
        await response.ShouldHaveStatus(HttpStatusCode.Created);
        Assert.Equal("Pending", (await response.Body()).Str("status"));
        var ids = (await response.Body()).GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).ToList();

        var pending = (await team.Manager.Client.GetJson("/api/remote-requests/pending")).EnumerateArray().Select(r => r.Int("id"));
        Assert.Equal(ids.Order(), pending.Order());
        await (await team.Manager.Client.ApproveRemote(ids[0])).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.RejectRemote(ids[1], "Sedinta de planificare")).ShouldHaveStatus(HttpStatusCode.OK);
        await _api.UpdateSettingsAsync();
    }

    [Fact]
    public async Task Weekly_limit_is_checked_again_when_pending_remote_days_are_approved()
    {
        await _api.UpdateSettingsAsync(remoteRequiresApproval: true);
        var team = await _api.CreateTeamAsync(employees: 1);
        var employee = team.Employees[0].Client;
        var ids = new List<int>();
        foreach (var day in new[] { Day(11, 16), Day(11, 17), Day(11, 18) })
        {
            ids.AddRange(await employee.CreateRemoteIds(day)); // cele în așteptare nu blochează crearea
        }

        await (await team.Manager.Client.ApproveRemote(ids[0])).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.ApproveRemote(ids[1])).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.ApproveRemote(ids[2])).ShouldFailWith(HttpStatusCode.Conflict, "REMOTE_WEEKLY_LIMIT_EXCEEDED");
        await _api.UpdateSettingsAsync();
    }

    [Fact]
    public async Task Pending_remote_day_can_be_moved_to_another_single_day()
    {
        await _api.UpdateSettingsAsync(remoteRequiresApproval: true);
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        var id = (await employee.CreateRemoteIds(Day(11, 17)))[0];

        var twoDays = await employee.PutAsJsonAsync($"/api/remote-requests/{id}", new { days = new[] { Day(11, 18), Day(11, 19) } });
        await twoDays.ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_EDIT_SINGLE_DAY");
        var moved = await employee.PutAsJsonAsync($"/api/remote-requests/{id}", new { days = new[] { Day(11, 19) }, comment = "Mutat" });
        await moved.ShouldHaveStatus(HttpStatusCode.OK);

        var own = Assert.Single((await employee.GetJson("/api/remote-requests")).EnumerateArray());
        Assert.Equal(Day(11, 19), Assert.Single(own.GetProperty("days").EnumerateArray()).GetString());
        Assert.Equal("Mutat", own.Str("comment"));
        await _api.UpdateSettingsAsync();
    }

    [Fact]
    public async Task Changed_settings_apply_immediately_to_new_requests_only()
    {
        await _api.UpdateSettingsAsync();
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        var existing = await employee.CreateLeaveId(Day(11, 23), Day(11, 23)); // preaviz 3 zile la creare

        await _api.UpdateSettingsAsync(leaveMinDaysAhead: 20, remoteMaxDaysAhead: 5);

        await (await employee.RequestLeave(Day(11, 24), Day(11, 24))).ShouldFailWith(HttpStatusCode.BadRequest, "LEAVE_DATE_OUT_OF_RANGE");
        await (await employee.RequestRemote(Day(11, 19))).ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_DATE_OUT_OF_RANGE");
        // Cererea existentă păstrează regulile de la creare: poate fi editată în continuare cu preavizul vechi.
        var edit = await employee.PutAsJsonAsync($"/api/leave-requests/{existing}", new { startDate = Day(11, 24), endDate = Day(11, 24), type = "Vacation" });
        await edit.ShouldHaveStatus(HttpStatusCode.OK);
        await _api.UpdateSettingsAsync();
    }

    [Fact]
    public async Task Turning_auto_approval_on_does_not_change_days_already_pending()
    {
        await _api.UpdateSettingsAsync(remoteRequiresApproval: true);
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0].Client;
        await employee.CreateRemoteIds(Day(11, 17));

        await _api.UpdateSettingsAsync(remoteRequiresApproval: false);

        Assert.Equal("Pending", Assert.Single((await employee.GetJson("/api/remote-requests")).EnumerateArray()).Str("status"));
    }

    [Fact]
    public async Task Team_absence_percentage_is_configurable()
    {
        await _api.UpdateSettingsAsync(leaveMaxTeamAbsentPct: 100);
        var team = await _api.CreateTeamAsync(employees: 1);
        var first = await team.Employees[0].Client.CreateLeaveId(Day(12, 7), Day(12, 7));
        await (await team.Manager.Client.ApproveLeave(first)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await team.Manager.Client.RequestLeave(Day(12, 7), Day(12, 7))).ShouldHaveStatus(HttpStatusCode.Created);
        await _api.UpdateSettingsAsync();
    }

    [Fact]
    public async Task Added_public_holidays_affect_new_requests_but_not_existing_ones()
    {
        await _api.UpdateSettingsAsync();
        var admin = await _api.AdminAsync();
        var team = await _api.CreateTeamAsync(employees: 2);
        var (first, second) = (team.Employees[0].Client, team.Employees[1].Client);
        var existing = await first.CreateLeaveId(Day(12, 7), Day(12, 11), "Unpaid");
        var original = (await admin.Client.GetJson("/api/settings")).GetProperty("holidays")
            .EnumerateArray().Select(h => new { date = h.Str("date"), name = h.Str("name") }).ToList();

        var withExtra = original.Append(new { date = Day(12, 9), name = "Zi liberă test" }).ToList();
        await (await admin.Client.PutAsJsonAsync("/api/settings/holidays", withExtra)).ShouldHaveStatus(HttpStatusCode.NoContent);
        try
        {
            await (await second.RequestRemote(Day(12, 9))).ShouldFailWith(HttpStatusCode.BadRequest, "REMOTE_HOLIDAY_FORBIDDEN");
            var created = await second.RequestLeave(Day(12, 8), Day(12, 10), "Unpaid");
            await created.ShouldHaveStatus(HttpStatusCode.Created);
            Assert.Equal(2, (await created.Body()).Int("workingDays"));

            var own = (await first.GetJson("/api/leave-requests")).EnumerateArray().Single(r => r.Int("id") == existing);
            Assert.Equal(5, own.Int("workingDays"));
        }
        finally
        {
            await (await admin.Client.PutAsJsonAsync("/api/settings/holidays", original)).ShouldHaveStatus(HttpStatusCode.NoContent);
        }
    }

    // Cerința 17: la crearea angajatului se preia valoarea implicită globală.
    [Fact]
    public async Task New_employees_get_the_default_annual_days_in_force_at_creation()
    {
        await _api.UpdateSettingsAsync(leaveDefaultAnnualDays: 25);
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];
        await _api.UpdateSettingsAsync(leaveDefaultAnnualDays: 21);

        Assert.Equal(25, (await employee.Client.Balance()).Int("totalDays"));
    }

}
