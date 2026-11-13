using System.Net;
using System.Net.Http.Json;
using System.Text;
using LeaveRemote.Api.Tests.Infrastructure;
using static LeaveRemote.Api.Tests.Infrastructure.HttpExtensions;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>Cerința 6 (opțională), a doua parte: exportul propriilor zile aprobate (.ics și .csv).</summary>
public class ExportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    /// <summary>Un angajat cu: concediu aprobat 7–9.12 (cu comentariu), concediu în așteptare, unul respins și două zile remote aprobate.</summary>
    private async Task<(TeamSetup Team, Member Employee)> EmployeeWithRequestsAsync()
    {
        var team = await _api.CreateTeamAsync(employees: 2);
        var employee = team.Employees[0];
        var approved = await employee.Client.CreateLeaveId(Day(12, 7), Day(12, 9));
        await (await employee.Client.PutAsJsonAsync($"/api/leave-requests/{approved}",
            new { startDate = Day(12, 7), endDate = Day(12, 9), type = "Vacation", comment = "Munte; schi, \"Poiana\"" })).ShouldHaveStatus(HttpStatusCode.OK);
        await (await team.Manager.Client.ApproveLeave(approved)).ShouldHaveStatus(HttpStatusCode.OK);
        await employee.Client.CreateLeaveId(Day(12, 14), Day(12, 14), "Unpaid");
        var rejected = await employee.Client.CreateLeaveId(Day(12, 15), Day(12, 15), "Special");
        await (await team.Manager.Client.RejectLeave(rejected, "Inventar anual")).ShouldHaveStatus(HttpStatusCode.OK);
        await employee.Client.CreateRemoteIds(Day(11, 17), Day(11, 19));

        // Colegul are și el un concediu aprobat, care nu trebuie să apară în exportul angajatului.
        var colleague = await team.Employees[1].Client.CreateLeaveId(Day(12, 21), Day(12, 21), "Medical");
        await (await team.Manager.Client.ApproveLeave(colleague)).ShouldHaveStatus(HttpStatusCode.OK);
        return (team, employee);
    }

    [Fact]
    public async Task ICalendar_export_contains_only_own_approved_days_as_all_day_events()
    {
        var (_, employee) = await EmployeeWithRequestsAsync();

        var response = await employee.Client.GetAsync("/api/exports/my-approved-days.ics");

        await response.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("zile-aprobate.ics", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var ics = await response.Content.ReadAsStringAsync();
        var unfolded = ics.Replace("\r\n ", string.Empty);

        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.Equal(3, CountOf(ics, "BEGIN:VEVENT"));
        // Concediul 7–9.12: DTEND este exclusiv, deci 10.12.
        Assert.Contains("DTSTART;VALUE=DATE:20261207\r\nDTEND;VALUE=DATE:20261210", ics);
        Assert.Contains("SUMMARY:Concediu de odihnă", unfolded);
        Assert.Contains("DESCRIPTION:3 zile lucrătoare\\nComentariu: Munte\\; schi\\, \"Poiana\"", unfolded);
        Assert.Contains("DTSTART;VALUE=DATE:20261117", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261119", ics);
        Assert.Equal(2, CountOf(unfolded, "SUMMARY:Lucru remote"));
        // Nu apar: cererea în așteptare (14.12), cea respinsă (15.12) și concediul colegului (21.12).
        Assert.DoesNotContain("20261214", ics);
        Assert.DoesNotContain("20261215", ics);
        Assert.DoesNotContain("20261221", ics);
    }

    [Fact]
    public async Task ICalendar_lines_follow_the_standard_format()
    {
        var (_, employee) = await EmployeeWithRequestsAsync();

        var ics = await (await employee.Client.GetAsync("/api/exports/my-approved-days.ics")).Content.ReadAsStringAsync();

        var lines = ics.Split("\r\n");
        Assert.All(lines, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"Linie prea lungă: {line}"));
        Assert.DoesNotContain('\n', ics.Replace("\r\n", string.Empty));
        Assert.Equal(CountOf(ics, "BEGIN:VEVENT"), CountOf(ics, "UID:"));
    }

    [Fact]
    public async Task Csv_export_is_a_table_of_own_approved_days_for_excel()
    {
        var (_, employee) = await EmployeeWithRequestsAsync();

        var response = await employee.Client.GetAsync("/api/exports/my-approved-days.csv");

        await response.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
        var rows = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            "Tip;Început;Sfârșit;Zile lucrătoare;Comentariu",
            "Lucru remote;17.11.2026;17.11.2026;1;",
            "Lucru remote;19.11.2026;19.11.2026;1;",
            "Concediu de odihnă;07.12.2026;09.12.2026;3;\"Munte; schi, \"\"Poiana\"\"\""
        ], rows);
    }

    [Fact]
    public async Task An_employee_without_approved_days_gets_empty_but_valid_files()
    {
        var employee = (await _api.CreateTeamAsync(employees: 1)).Employees[0];

        var ics = await (await employee.Client.GetAsync("/api/exports/my-approved-days.ics")).Content.ReadAsStringAsync();
        var csv = await (await employee.Client.GetAsync("/api/exports/my-approved-days.csv")).Content.ReadAsStringAsync();

        Assert.Equal(0, CountOf(ics, "BEGIN:VEVENT"));
        Assert.Contains("END:VCALENDAR", ics);
        Assert.Single(csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Exports_require_authentication()
    {
        var anonymous = factory.CreateClient();

        await (await anonymous.GetAsync("/api/exports/my-approved-days.ics")).ShouldFailWith(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        await (await anonymous.GetAsync("/api/exports/my-approved-days.csv")).ShouldFailWith(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    private static int CountOf(string text, string value) =>
        (text.Length - text.Replace(value, string.Empty).Length) / value.Length;
}
