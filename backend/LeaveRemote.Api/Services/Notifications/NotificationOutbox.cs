namespace LeaveRemote.Api.Services.Notifications;

/// <summary>
/// Coada de emailuri a request-ului HTTP curent. Emailurile sunt doar adunate în timpul operației
/// și sunt predate poștașului abia după ce modificările au fost salvate definitiv în baza de date;
/// dacă operația eșuează, coada este aruncată și nu pleacă nimic.
/// </summary>
public sealed class NotificationOutbox(IEmailSender sender, ILogger<NotificationOutbox> logger)
{
    private readonly List<EmailMessage> _pending = [];

    public IReadOnlyList<EmailMessage> Pending => _pending;

    public void Enqueue(EmailMessage message) => _pending.Add(message);

    public void Discard() => _pending.Clear();

    /// <summary>
    /// Trimite emailurile adunate. Operația a fost deja salvată, deci o eroare la trimitere
    /// este doar înregistrată în log și nu anulează cererea utilizatorului.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var batch = _pending.ToList();
        _pending.Clear();
        foreach (var message in batch)
        {
            try
            {
                await sender.SendAsync(message, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Emailul către {To} cu subiectul „{Subject}” nu a putut fi trimis.",
                    message.To, message.Subject);
            }
        }
    }
}
