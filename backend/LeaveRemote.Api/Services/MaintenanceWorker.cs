namespace LeaveRemote.Api.Services;

/// <summary>
/// Întreținerea automată a datelor, la pornirea aplicației și apoi periodic
/// (implicit la fiecare oră, configurabil prin „Maintenance:IntervalMinutes”):
/// <list type="bullet">
/// <item>generează sărbătorile legale pentru anii în care se pot face cereri (ex. la schimbarea anului);</item>
/// <item>anulează cererile rămase „În așteptare” după finalul perioadei lor.</item>
/// </list>
/// O eroare (ex. baza de date indisponibilă) nu oprește aplicația; sarcina se reîncearcă la următoarea rulare.
/// </summary>
public sealed class MaintenanceWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, configuration.GetValue("Maintenance:IntervalMinutes", 60)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunAsync("generarea sărbătorilor legale", (services, token) =>
                services.GetRequiredService<PublicHolidayService>().EnsureCoveredAsync(token), stoppingToken);
            await RunAsync("expirarea cererilor nedecise", (services, token) =>
                services.GetRequiredService<PendingRequestExpiryService>().ExpireAsync(token), stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(string name, Func<IServiceProvider, CancellationToken, Task> task, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await task(scope.ServiceProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Întreținerea automată ({Task}) a eșuat.", name);
        }
    }
}
