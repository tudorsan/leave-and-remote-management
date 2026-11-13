namespace LeaveRemote.Api.Services;

/// <summary>
/// Eroare de business: statusul HTTP, codul stabil (pentru client) și mesajul lizibil.
/// Serviciile nu cunosc HTTP-ul direct; controllerele traduc eroarea în răspuns.
/// </summary>
public sealed record ServiceError(int StatusCode, string Code, string Message)
{
    public static ServiceError BadRequest(string code, string message) => new(StatusCodes.Status400BadRequest, code, message);

    public static ServiceError Unauthorized(string code, string message) => new(StatusCodes.Status401Unauthorized, code, message);

    public static ServiceError Forbidden(string code = "FORBIDDEN", string message = "Nu ai permisiunea pentru această operație.") =>
        new(StatusCodes.Status403Forbidden, code, message);

    public static ServiceError NotFound(string code, string message) => new(StatusCodes.Status404NotFound, code, message);

    public static ServiceError Conflict(string code, string message) => new(StatusCodes.Status409Conflict, code, message);

    public static ServiceError Internal(string code, string message) => new(StatusCodes.Status500InternalServerError, code, message);
}

/// <summary>Rezultatul unei operații: fie o valoare, fie o <see cref="ServiceError"/>.</summary>
public sealed class Result<T>
{
    private Result(T? value, ServiceError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ServiceError? Error { get; }

    public static implicit operator Result<T>(T value) => new(value, null);

    public static implicit operator Result<T>(ServiceError error) => new(default, error);
}
