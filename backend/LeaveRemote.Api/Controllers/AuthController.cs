using System.Security.Claims;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

[Route("api/auth")]
public class AuthController(AuthService auth, PasswordResetService passwordReset) : ApiControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request) =>
        Respond(await auth.LoginAsync(request));

    /// <summary>Identitatea din tokenul curent (rolul este reîncărcat din baza de date la fiecare cerere).</summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        isAuthenticated = User.Identity?.IsAuthenticated,
        authenticationType = User.Identity?.AuthenticationType,
        userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
        role = User.FindFirstValue(ClaimTypes.Role)
    });

    /// <summary>
    /// Cere un link de resetare a parolei. Răspunsul este același dacă adresa există sau nu,
    /// ca să nu se poată afla ce adrese au cont.
    /// </summary>
    [HttpPost("forgot-password")]
    [RequestWriteTransaction]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordInput input)
    {
        await passwordReset.RequestAsync(input.Email);
        return Accepted(new
        {
            message = "Dacă adresa aparține unui cont activ, vei primi un email cu un link de resetare a parolei."
        });
    }

    /// <summary>Setează parola nouă folosind tokenul din linkul primit pe email.</summary>
    [HttpPost("reset-password")]
    [RequestWriteTransaction]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordInput input) =>
        await passwordReset.ResetAsync(input.Token, input.NewPassword) is { } error
            ? ErrorResult(error)
            : Ok(new { message = "Parola a fost schimbată. Te poți autentifica cu parola nouă." });
}
