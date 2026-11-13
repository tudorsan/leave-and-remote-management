using System.Security.Claims;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

[Route("api/auth")]
public class AuthController(AuthService auth) : ApiControllerBase
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
}
