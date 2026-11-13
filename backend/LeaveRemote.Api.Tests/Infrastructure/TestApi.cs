using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LeaveRemote.Api.Tests.Infrastructure;

/// <summary>Un utilizator creat pentru test, cu clientul HTTP deja autentificat.</summary>
public sealed record Member(int Id, string Email, string Role, HttpClient Client);

/// <summary>O echipă izolată: un Manager (subordonat Adminului) și angajații lui.</summary>
public sealed record TeamSetup(int TeamId, Member Manager, IReadOnlyList<Member> Employees);

/// <summary>
/// Operații de nivel înalt peste API, folosite de toate testele de integrare.
/// Fiecare test își creează propria echipă și proprii utilizatori, ca testele să nu se influențeze.
/// </summary>
public sealed class TestApi(ApiFactory factory)
{
    public const string Password = "Parola123!";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ApiFactory Factory => factory;

    public async Task<(int UserId, HttpClient Client)> LoginAsync(string email, string password)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        await response.ShouldHaveStatus(HttpStatusCode.OK);
        var body = await response.Body();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return (body.GetProperty("userId").GetInt32(), client);
    }

    public async Task<Member> AdminAsync()
    {
        var (id, client) = await LoginAsync("admin@example.com", "Admin123!");
        return new Member(id, "admin@example.com", "Admin", client);
    }

    public async Task<int> CreateTeamAsync()
    {
        var admin = await AdminAsync();
        var response = await admin.Client.PostAsJsonAsync("/api/teams", new { name = $"Echipa {Guid.NewGuid():N}" });
        await response.ShouldHaveStatus(HttpStatusCode.Created);
        return (await response.Body()).GetProperty("id").GetInt32();
    }

    public async Task<Member> CreateUserAsync(string role, int? teamId, int? managerId)
    {
        var admin = await AdminAsync();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.ro";
        var response = await admin.Client.PostAsJsonAsync("/api/users", new
        {
            firstName = role, lastName = "Test", email, password = Password, role, teamId, managerId
        });
        await response.ShouldHaveStatus(HttpStatusCode.Created);
        var (id, client) = await LoginAsync(email, Password);
        return new Member(id, email, role, client);
    }

    /// <summary>Echipă nouă cu un Manager (al cărui superior este Adminul) și <paramref name="employees"/> angajați.</summary>
    public async Task<TeamSetup> CreateTeamAsync(int employees)
    {
        var admin = await AdminAsync();
        var teamId = await CreateTeamAsync();
        var manager = await CreateUserAsync("Manager", teamId, admin.Id);
        var members = new List<Member>();
        for (var i = 0; i < employees; i++) members.Add(await CreateUserAsync("Employee", teamId, manager.Id));
        return new TeamSetup(teamId, manager, members);
    }

    /// <summary>Setează toate setările globale: valorile implicite din PDF, cu modificările date.</summary>
    public async Task UpdateSettingsAsync(
        int remoteMinDaysAhead = 0, int remoteMaxDaysAhead = 30, int remoteMaxDaysPerWeek = 2,
        bool remoteRequiresApproval = false, int leaveMinDaysAhead = 3, int leaveMaxDaysAhead = 365,
        int leaveDefaultAnnualDays = 21, int leaveMaxTeamAbsentPct = 50)
    {
        var admin = await AdminAsync();
        var response = await admin.Client.PutAsJsonAsync("/api/settings", new
        {
            remoteMinDaysAhead, remoteMaxDaysAhead, remoteMaxDaysPerWeek, remoteRequiresApproval,
            leaveMinDaysAhead, leaveMaxDaysAhead, leaveDefaultAnnualDays, leaveMaxTeamAbsentPct
        });
        await response.ShouldHaveStatus(HttpStatusCode.OK);
    }
}

public static class HttpExtensions
{
    public static string Day(int month, int day, int year = 2026) => new DateOnly(year, month, day).ToString("yyyy-MM-dd");

    public static Task<HttpResponseMessage> RequestLeave(this HttpClient client, string start, string end,
        string type = "Vacation", string? comment = null) =>
        client.PostAsJsonAsync("/api/leave-requests", new { startDate = start, endDate = end, type, comment });

    public static Task<HttpResponseMessage> RequestRemote(this HttpClient client, params string[] days) =>
        client.PostAsJsonAsync("/api/remote-requests", new { days });

    public static Task<HttpResponseMessage> Patch(this HttpClient client, string url, object? body = null) =>
        client.PatchAsync(url, body is null ? null : JsonContent.Create(body));

    public static Task<HttpResponseMessage> ApproveLeave(this HttpClient client, int id) =>
        client.Patch($"/api/leave-requests/{id}/approve");

    public static Task<HttpResponseMessage> RejectLeave(this HttpClient client, int id, string reason) =>
        client.Patch($"/api/leave-requests/{id}/reject", new { reason });

    public static Task<HttpResponseMessage> CancelLeave(this HttpClient client, int id) =>
        client.Patch($"/api/leave-requests/{id}/cancel");

    public static Task<HttpResponseMessage> ApproveRemote(this HttpClient client, int id) =>
        client.Patch($"/api/remote-requests/{id}/approve");

    public static Task<HttpResponseMessage> RejectRemote(this HttpClient client, int id, string reason) =>
        client.Patch($"/api/remote-requests/{id}/reject", new { reason });

    public static Task<HttpResponseMessage> CancelRemote(this HttpClient client, int id) =>
        client.Patch($"/api/remote-requests/{id}/cancel");

    /// <summary>Creează o cerere de concediu care trebuie să reușească și întoarce id-ul ei.</summary>
    public static async Task<int> CreateLeaveId(this HttpClient client, string start, string end, string type = "Vacation")
    {
        var response = await client.RequestLeave(start, end, type);
        await response.ShouldHaveStatus(HttpStatusCode.Created);
        return (await response.Body()).GetProperty("id").GetInt32();
    }

    /// <summary>Creează zile remote care trebuie să reușească și întoarce id-urile cererilor (una per zi).</summary>
    public static async Task<List<int>> CreateRemoteIds(this HttpClient client, params string[] days)
    {
        var response = await client.RequestRemote(days);
        await response.ShouldHaveStatus(HttpStatusCode.Created);
        return (await response.Body()).GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).ToList();
    }

    public static async Task<JsonElement> GetJson(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        await response.ShouldHaveStatus(HttpStatusCode.OK);
        return await response.Body();
    }

    public static async Task<JsonElement> Balance(this HttpClient client, int? userId = null, int? year = null)
    {
        var url = userId is null ? "/api/leave-balances/me" : $"/api/leave-balances/{userId}";
        return await client.GetJson(year is null ? url : $"{url}?year={year}");
    }

    public static async Task<JsonElement> Body(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task ShouldHaveStatus(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Așteptat {(int)expected} {expected}, primit {(int)response.StatusCode}: {body}");
        }
    }

    /// <summary>Verifică statusul și formatul unic de eroare { code, message } cu un mesaj nevid.</summary>
    public static async Task ShouldFailWith(this HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        await response.ShouldHaveStatus(expected);
        var body = await response.Body();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    public static string Str(this JsonElement element, string property) => element.GetProperty(property).GetString()!;

    public static int Int(this JsonElement element, string property) => element.GetProperty(property).GetInt32();
}
