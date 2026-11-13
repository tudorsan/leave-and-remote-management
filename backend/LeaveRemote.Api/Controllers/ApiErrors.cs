using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

/// <summary>
/// Răspunsuri de eroare cu formatul unic al API-ului: { code, message }.
/// </summary>
internal static class ApiErrors
{
    public const string ForbiddenMessage = "Nu ai permisiunea pentru această operație.";
    public const string UnauthenticatedMessage = "Sesiunea a expirat sau este invalidă. Autentifică-te din nou.";

    public static ObjectResult Forbidden(string message = ForbiddenMessage) =>
        Error(StatusCodes.Status403Forbidden, "FORBIDDEN", message);

    public static ObjectResult Unauthenticated() =>
        Error(StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", UnauthenticatedMessage);

    public static ObjectResult NotFound(string code, string message) =>
        Error(StatusCodes.Status404NotFound, code, message);

    public static ObjectResult Error(int statusCode, string code, string message) =>
        new(new { code, message }) { StatusCode = statusCode };

    /// <summary>
    /// Codul și mesajul folosite când răspunsul a fost produs de framework fără corp
    /// (autentificare lipsă, rol insuficient, rută inexistentă etc.).
    /// </summary>
    public static (string Code, string Message) ForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => ("INVALID_INPUT", "Datele trimise lipsesc sau au un format invalid."),
        StatusCodes.Status401Unauthorized => ("UNAUTHENTICATED", UnauthenticatedMessage),
        StatusCodes.Status403Forbidden => ("FORBIDDEN", ForbiddenMessage),
        StatusCodes.Status404NotFound => ("NOT_FOUND", "Resursa cerută nu există."),
        StatusCodes.Status405MethodNotAllowed => ("METHOD_NOT_ALLOWED", "Operația nu este permisă pentru această resursă."),
        StatusCodes.Status415UnsupportedMediaType => ("UNSUPPORTED_MEDIA_TYPE", "Cererea trebuie trimisă ca JSON."),
        >= 500 => ("INTERNAL_ERROR", "A apărut o eroare neașteptată. Încearcă din nou."),
        _ => ("REQUEST_FAILED", "Cererea nu a putut fi procesată.")
    };
}
