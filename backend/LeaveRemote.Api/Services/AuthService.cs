using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LeaveRemote.Api.Controllers;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.DTOs;
using LeaveRemote.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace LeaveRemote.Api.Services;

/// <summary>Autentificare cu email și parolă (hash bcrypt) și emiterea tokenului JWT.</summary>
public sealed class AuthService(AppDbContext db, IConfiguration configuration)
{
    public const string SecurityStampClaim = "security_stamp";

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return ServiceError.BadRequest("INVALID_INPUT", "Emailul și parola sunt obligatorii.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Același mesaj pentru email necunoscut, cont inactiv sau parolă greșită: nu dezvăluim care conturi există.
        if (!ApiInput.FitsBCrypt(request.Password) || user is null || !user.IsActive ||
            !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return ServiceError.Unauthorized("INVALID_CREDENTIALS", "Email sau parolă incorectă.");
        }

        return new LoginResponse
        {
            Token = CreateToken(user),
            UserId = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = user.Role.ToString(),
            TeamId = user.TeamId
        };
    }

    private string CreateToken(User user)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT secret key is missing.");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            // Dacă parola se schimbă, valoarea din baza de date se schimbă și tokenul devine invalid.
            new(SecurityStampClaim, user.SecurityStamp)
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            // Expirarea folosește ora reală, aceeași cu care middleware-ul JWT validează tokenul
            // (ceasul aplicației servește doar regulilor de business pe date calendaristice).
            expires: DateTime.UtcNow.AddMinutes(configuration.GetValue("Jwt:ExpirationMinutes", 60)),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
