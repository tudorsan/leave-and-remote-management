using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Services;
using LeaveRemote.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LeaveRemote.Api.Tests.Integration;

/// <summary>Cerința 3 (opțională): resetarea parolei prin link trimis pe email (simulat în log).</summary>
public partial class PasswordResetTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string NewPassword = "ParolaNoua456!";
    private readonly TestApi _api = new(factory);

    [GeneratedRegex(@"/reset-password\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex ResetLink();

    private async Task<HttpResponseMessage> ForgotAsync(string email) =>
        await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { email });

    private async Task<HttpResponseMessage> ResetAsync(string token, string newPassword = NewPassword) =>
        await factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword });

    private async Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>Cere resetarea și întoarce tokenul din ultimul email primit.</summary>
    private async Task<string> RequestTokenAsync(string email)
    {
        await (await ForgotAsync(email)).ShouldHaveStatus(HttpStatusCode.Accepted);
        var mail = factory.Emails.SentTo(email).Last(m => m.Subject == "Resetarea parolei");
        return ResetLink().Match(mail.Body).Groups[1].Value;
    }

    [Fact]
    public async Task Forgotten_password_is_reset_through_the_emailed_link()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);

        var token = await RequestTokenAsync(user.Email);
        await (await ResetAsync(token)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await LoginAsync(user.Email, NewPassword)).ShouldHaveStatus(HttpStatusCode.OK);
        await (await LoginAsync(user.Email, TestApi.Password)).ShouldFailWith(HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task The_email_contains_a_link_to_the_frontend_reset_page()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);

        await RequestTokenAsync(user.Email);

        var mail = factory.Emails.SentTo(user.Email).Last();
        Assert.Contains("http://localhost:5173/reset-password?token=", mail.Body);
        Assert.Contains("expiră în 30 de minute", mail.Body);
    }

    [Fact]
    public async Task The_response_does_not_reveal_whether_the_email_has_an_account()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);

        var known = await ForgotAsync(user.Email);
        var unknown = await ForgotAsync("nimeni-" + Guid.NewGuid().ToString("N") + "@test.ro");

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal((await known.Body()).Str("message"), (await unknown.Body()).Str("message"));
    }

    [Fact]
    public async Task Only_the_hash_of_the_token_is_stored()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);

        var token = await RequestTokenAsync(user.Email);

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PasswordResetTokens
            .Where(t => t.UserId == user.Id).Select(t => t.TokenHash).ToListAsync();
        var hash = Assert.Single(stored);
        Assert.NotEqual(token, hash);
        Assert.Equal(PasswordResetService.Hash(token), hash);
        Assert.True(token.Length >= 43, "tokenul trebuie să aibă cel puțin 32 de octeți aleatori");
    }

    [Fact]
    public async Task A_link_can_be_used_only_once()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);
        var token = await RequestTokenAsync(user.Email);

        await (await ResetAsync(token)).ShouldHaveStatus(HttpStatusCode.OK);

        await (await ResetAsync(token, "AltaParola789!")).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");
    }

    [Fact]
    public async Task A_link_expires_after_thirty_minutes()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);
        var token = await RequestTokenAsync(user.Email);

        factory.Clock.UtcNow = factory.Clock.UtcNow.AddMinutes(31);
        try
        {
            await (await ResetAsync(token)).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");
        }
        finally
        {
            factory.ResetToday();
        }
    }

    [Fact]
    public async Task Requesting_a_new_link_invalidates_the_previous_one()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);
        var first = await RequestTokenAsync(user.Email);
        var second = await RequestTokenAsync(user.Email);

        await (await ResetAsync(first)).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");
        await (await ResetAsync(second)).ShouldHaveStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Made_up_tokens_are_rejected()
    {
        await (await ResetAsync("token-inventat")).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");
        await (await ResetAsync("")).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");
    }

    [Fact]
    public async Task A_weak_new_password_is_rejected_and_the_link_stays_usable()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);
        var token = await RequestTokenAsync(user.Email);

        await (await ResetAsync(token, "123")).ShouldFailWith(HttpStatusCode.BadRequest, "INVALID_PASSWORD");

        await (await ResetAsync(token)).ShouldHaveStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Resetting_the_password_signs_out_existing_sessions()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);
        await (await user.Client.GetAsync("/api/calendar")).ShouldHaveStatus(HttpStatusCode.OK);

        await (await ResetAsync(await RequestTokenAsync(user.Email))).ShouldHaveStatus(HttpStatusCode.OK);

        await (await user.Client.GetAsync("/api/calendar")).ShouldFailWith(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        var (_, fresh) = await _api.LoginAsync(user.Email, NewPassword);
        await (await fresh.GetAsync("/api/calendar")).ShouldHaveStatus(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_user_is_told_by_email_that_the_password_changed()
    {
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);

        await (await ResetAsync(await RequestTokenAsync(user.Email))).ShouldHaveStatus(HttpStatusCode.OK);

        Assert.Equal("Parola ta a fost schimbată", factory.Emails.SentTo(user.Email).Last().Subject);
    }

    [Fact]
    public async Task Deactivated_accounts_get_no_reset_link()
    {
        var admin = await _api.AdminAsync();
        var user = await _api.CreateUserAsync("Employee", await _api.CreateTeamAsync(), null);
        await (await admin.Client.Patch($"/api/users/{user.Id}/deactivate")).ShouldHaveStatus(HttpStatusCode.OK);

        await (await ForgotAsync(user.Email)).ShouldHaveStatus(HttpStatusCode.Accepted);

        Assert.DoesNotContain(factory.Emails.SentTo(user.Email), m => m.Subject == "Resetarea parolei");
    }
}
