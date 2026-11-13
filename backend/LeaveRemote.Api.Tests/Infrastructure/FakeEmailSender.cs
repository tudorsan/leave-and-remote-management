using System.Collections.Concurrent;
using LeaveRemote.Api.Services.Notifications;

namespace LeaveRemote.Api.Tests.Infrastructure;

/// <summary>„Poștașul fals” din teste: nu trimite nimic, doar păstrează emailurile ca testele să le verifice.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> SentTo(string email) =>
        _sent.Where(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
