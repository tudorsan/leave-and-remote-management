using LeaveRemote.Api.Data;
using LeaveRemote.Api.Models;
using LeaveRemote.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Services;

/// <summary>
/// Anulează automat cererile rămase „În așteptare” după ce a trecut ultima zi a perioadei lor.
/// Cât timp perioada nu s-a încheiat, cererea poate fi încă decisă (ex. un concediu medical anunțat
/// după ce a început). Expirarea folosește starea „Anulată” din diagrama PDF (nu inventează o stare nouă),
/// păstrează motivul, eliberează soldul rezervat și îl anunță pe solicitant prin email.
/// </summary>
public sealed class PendingRequestExpiryService(
    AppDbContext db,
    NotificationService notifications,
    NotificationOutbox outbox,
    AppClock clock,
    ILogger<PendingRequestExpiryService> logger)
{
    public const string ExpiredReason = "Anulată automat: cererea nu a fost decisă până la finalul perioadei.";

    /// <summary>Întoarce numărul de cereri anulate.</summary>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await RequestWriteLock.AcquireAsync(db, cancellationToken))
        {
            // O altă operație ține lock-ul; încercăm din nou la următoarea rulare.
            return 0;
        }

        var leave = await db.LeaveRequests
            .Where(r => r.Status == RequestStatus.Pending && r.EndDate < today)
            .ToListAsync(cancellationToken);
        var remote = await db.RemoteRequests.Include(r => r.Days)
            .Where(r => r.Status == RequestStatus.Pending && r.Days.All(d => d.Date < today))
            .ToListAsync(cancellationToken);
        if (leave.Count == 0 && remote.Count == 0) return 0;

        var userIds = leave.Select(r => r.UserId).Concat(remote.Select(r => r.UserId)).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, cancellationToken);

        foreach (var request in leave)
        {
            request.Status = RequestStatus.Canceled;
            request.CancellationReason = ExpiredReason;
            notifications.LeaveExpired(request, users[request.UserId]);
        }
        foreach (var request in remote)
        {
            request.Status = RequestStatus.Canceled;
            request.CancellationReason = ExpiredReason;
            notifications.RemoteExpired(request.Days.Select(d => d.Date), users[request.UserId]);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await outbox.FlushAsync(CancellationToken.None);

        var expired = leave.Count + remote.Count;
        logger.LogInformation("{Count} cereri nedecise până la finalul perioadei au fost anulate automat.", expired);
        return expired;
    }
}
