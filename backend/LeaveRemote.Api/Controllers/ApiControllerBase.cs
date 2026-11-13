using LeaveRemote.Api.Models;
using LeaveRemote.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeaveRemote.Api.Controllers;

/// <summary>
/// Baza controllerelor: încarcă utilizatorul activ curent și traduce rezultatele serviciilor în răspunsuri HTTP.
/// Controllerele nu conțin reguli de business.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Rulează acțiunea pentru utilizatorul activ curent sau întoarce 401 dacă nu există.</summary>
    protected async Task<IActionResult> AsCurrentUser(Func<User, Task<IActionResult>> action)
    {
        var current = await HttpContext.RequestServices.GetRequiredService<CurrentUserService>().GetAsync(User);
        return current.Error is { } error ? ErrorResult(error) : await action(current.Value!);
    }

    protected static IActionResult Respond<T>(Result<T> result, Func<T, IActionResult>? onSuccess = null) =>
        result.Error is { } error
            ? ErrorResult(error)
            : onSuccess?.Invoke(result.Value!) ?? new OkObjectResult(result.Value);

    protected static IActionResult Respond(ServiceError? error) =>
        error is null ? new NoContentResult() : ErrorResult(error);

    protected static IActionResult ErrorResult(ServiceError error) =>
        ApiErrors.Error(error.StatusCode, error.Code, error.Message);
}
