using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>Cerințele 1–2 (autentificare, dezactivare) și formatul unic de eroare { code, message }.</summary>
public class AuthAndErrorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly TestApi _api = new(factory);

    [Fact]
    public async Task Login_with_email_and_password_returns_a_jwt()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.com", password = "Admin123!" });

        await response.ShouldHaveStatus(HttpStatusCode.OK);
        var body = await response.Body();
        Assert.Equal(3, body.Str("token").Split('.').Length);
        Assert.Equal("Admin", body.Str("role"));
    }

    [Fact]
    public async Task Login_email_is_case_insensitive()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "  ADMIN@example.com ", password = "Admin123!" });

        await response.ShouldHaveStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_with_a_generic_message()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "admin@example.com", password = "gresita" });

        await response.ShouldFailWith(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Passwords_are_stored_as_bcrypt_hashes()
    {
        var member = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);

        using var scope = factory.Services.CreateScope();
        var hash = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .Where(u => u.Id == member.Id).Select(u => u.PasswordHash).SingleAsync();

        Assert.StartsWith("$2", hash);
        Assert.DoesNotContain(TestApi.Password, hash);
        Assert.True(BCrypt.Net.BCrypt.Verify(TestApi.Password, hash));
    }

    [Fact]
    public async Task A_deactivated_user_can_no_longer_log_in_and_existing_tokens_stop_working()
    {
        var admin = await _api.AdminAsync();
        var member = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);

        await (await admin.Client.Patch($"/api/users/{member.Id}/deactivate")).ShouldHaveStatus(HttpStatusCode.OK);

        await (await member.Client.GetAsync("/api/calendar")).ShouldFailWith(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = member.Email, password = TestApi.Password });
        await login.ShouldFailWith(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Requests_without_a_token_get_401_with_the_error_body()
    {
        await (await factory.CreateClient().GetAsync("/api/calendar")).ShouldFailWith(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_forged_token_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "abc.def.ghi");

        await (await client.GetAsync("/api/calendar")).ShouldFailWith(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Framework_errors_use_the_same_error_format()
    {
        var admin = await _api.AdminAsync();

        await (await admin.Client.GetAsync("/api/nu-exista")).ShouldFailWith(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await admin.Client.DeleteAsync("/api/calendar")).ShouldFailWith(HttpStatusCode.MethodNotAllowed, "METHOD_NOT_ALLOWED");
        var plainText = await admin.Client.PostAsync("/api/leave-requests", new StringContent("x"));
        await plainText.ShouldFailWith(HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
        var malformed = await admin.Client.PostAsync("/api/leave-requests",
            new StringContent("{bad", System.Text.Encoding.UTF8, "application/json"));
        await malformed.ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_INPUT");
    }
}
