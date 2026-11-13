using LeaveRemote.Api.Controllers;

namespace LeaveRemote.Api.Services;

/// <summary>Regulile unei parole noi, comune pentru crearea contului și resetarea parolei.</summary>
public static class PasswordRules
{
    public const int MinLength = 8;

    public static ServiceError? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return ServiceError.BadRequest("INVALID_PASSWORD", $"Parola trebuie să conțină minimum {MinLength} caractere.");
        }
        return ApiInput.FitsBCrypt(password)
            ? null
            : ServiceError.BadRequest("INVALID_PASSWORD",
                $"Parola poate avea maximum {ApiInput.BCryptMaxPasswordBytes} de octeți în UTF-8 și nu poate conține caracterul nul.");
    }
}
