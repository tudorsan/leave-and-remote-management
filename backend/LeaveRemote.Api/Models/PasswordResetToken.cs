namespace LeaveRemote.Api.Models;

/// <summary>
/// „Caietul” resetărilor de parolă (cerința 3): un rând per link de resetare trimis.
/// Tokenul din link nu este salvat; se păstrează doar amprenta lui SHA-256, deci un link
/// nu poate fi refăcut nici de cineva care citește baza de date.
/// </summary>
public class PasswordResetToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Amprenta SHA-256 a tokenului, în hexazecimal (64 de caractere).</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Momentul folosirii sau al invalidării; un token are o singură utilizare.</summary>
    public DateTime? UsedAt { get; set; }
}
