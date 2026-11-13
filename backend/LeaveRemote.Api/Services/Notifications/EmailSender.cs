namespace LeaveRemote.Api.Services.Notifications;

/// <summary>Un email gata compus: destinatar, subiect și text.</summary>
public sealed record EmailMessage(string To, string Subject, string Body);

/// <summary>
/// „Poștașul”: trimite un email deja compus. Restul aplicației depinde doar de această interfață,
/// deci implementarea (log acum, SMTP mai târziu) se poate înlocui fără alte modificări.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementarea folosită în aplicație: emailul nu pleacă prin internet, ci este scris în log
/// (cerința 16 permite explicit notificarea „simulată în log”).
/// </summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "EMAIL SIMULAT\n  Către:   {To}\n  Subiect: {Subject}\n  ---\n{Body}",
            message.To, message.Subject, Indent(message.Body));
        return Task.CompletedTask;
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(line => "  " + line));
}
