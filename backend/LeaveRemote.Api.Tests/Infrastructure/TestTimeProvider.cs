namespace LeaveRemote.Api.Tests.Infrastructure;

/// <summary>Ceas controlat de teste; poate fi mutat înainte și înapoi.</summary>
public sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
