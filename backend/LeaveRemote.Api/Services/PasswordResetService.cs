using System.Security.Cryptography;
using System.Text;
using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using LeaveRemote.Api.Services.Notifications;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>
/// Cerința 3 (opțională): resetarea parolei prin link trimis pe email (simulat în log).
/// <list type="number">
/// <item>Utilizatorul cere resetarea cu adresa lui de email.</item>
/// <item>Se generează un token aleator; în baza de date se salvează doar amprenta lui (SHA-256),
/// legată de utilizator, cu expirare.</item>
/// <item>Linkul cu tokenul pleacă doar pe emailul utilizatorului.</item>
/// <item>Cu tokenul valid (existent, neexpirat, nefolosit) se alege o parolă nouă; tokenul devine folosit,
/// iar toate sesiunile existente sunt închise.</item>
/// </list>
/// </summary>
public sealed class PasswordResetService(
    AppDbContext db,
    NotificationOutbox outbox,
    IConfiguration configuration,
    AppClock clock)
{
    public const int TokenBytes = 32;

    private TimeSpan TokenLifetime =>
        TimeSpan.FromMinutes(configuration.GetValue("PasswordReset:TokenLifetimeMinutes", 30));

    /// <summary>
    /// Pornește resetarea. Răspunsul către client este mereu același, indiferent dacă adresa există,
    /// ca să nu se poată afla ce adrese au cont.
    /// </summary>
    public async Task RequestAsync(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized)) return;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized && u.IsActive);
        if (user is null) return;

        var now = clock.UtcNow;
        await InvalidateOpenTokensAsync(user.Id, now);

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(TokenLifetime)
        });
        await db.SaveChangesAsync();

        var minutes = (int)TokenLifetime.TotalMinutes;
        outbox.Enqueue(new EmailMessage(user.Email, "Resetarea parolei",
            $"""
            Salut, {user.FirstName}!

            Am primit o cerere de resetare a parolei pentru contul tău.
            Deschide linkul de mai jos ca să îți alegi o parolă nouă:
            {ResetLink(token)}

            Linkul poate fi folosit o singură dată și expiră în {minutes} de minute.
            Dacă nu tu ai cerut resetarea, ignoră acest email; parola ta rămâne neschimbată.
            """));
    }

    /// <summary>Setează parola nouă dacă tokenul este valid și închide toate sesiunile existente.</summary>
    public async Task<ServiceError?> ResetAsync(string? token, string? newPassword)
    {
        if (PasswordRules.Validate(newPassword) is { } passwordError) return passwordError;

        var now = clock.UtcNow;
        var hash = string.IsNullOrWhiteSpace(token) ? null : Hash(token.Trim());
        var entry = hash is null
            ? null
            : await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > now);
        var user = entry is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == entry.UserId && u.IsActive);
        if (entry is null || user is null)
        {
            return ServiceError.BadRequest("INVALID_RESET_TOKEN",
                "Linkul de resetare este invalid, a fost deja folosit sau a expirat. Cere un link nou.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        entry.UsedAt = now;
        await db.SaveChangesAsync();
        await InvalidateOpenTokensAsync(user.Id, now);

        outbox.Enqueue(new EmailMessage(user.Email, "Parola ta a fost schimbată",
            $"""
            Salut, {user.FirstName}!

            Parola contului tău a fost schimbată, iar toate sesiunile deschise au fost închise.
            Dacă nu tu ai făcut această schimbare, contactează imediat un administrator.
            """));
        return null;
    }

    /// <summary>Amprenta SHA-256 (hex) a tokenului; doar ea ajunge în baza de date.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string ResetLink(string token)
    {
        var baseUrl = (configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        return $"{baseUrl}/reset-password?token={token}";
    }

    /// <summary>Un link nou (sau o parolă schimbată) anulează linkurile rămase nefolosite.</summary>
    private Task InvalidateOpenTokensAsync(int userId, DateTime now) =>
        db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now));
}
