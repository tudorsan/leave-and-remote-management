using LeaveRemote.Api.Controllers;
using LeaveRemote.Api.Services;
using LeaveRemote.Api.Tests.Infrastructure;

namespace LeaveRemote.Api.Tests.Unit;

public class AppClockAndInputTests
{
    /// <summary>Regula 9: „azi” este o dată calendaristică în ora României, indiferent de fusul serverului.</summary>
    [Theory]
    [InlineData("2026-11-13T21:30:00Z", "2026-11-13")] // 23:30 în România
    [InlineData("2026-11-13T22:30:00Z", "2026-11-14")] // 00:30 în România
    [InlineData("2026-07-01T21:30:00Z", "2026-07-02")] // ora de vară: 00:30 în România
    public void Today_is_the_calendar_date_in_Romania(string utc, string expected)
    {
        var clock = new AppClock(new TestTimeProvider(DateTimeOffset.Parse(utc)));

        Assert.Equal(DateOnly.Parse(expected), clock.Today);
    }

    /// <summary>Regula 8: respingerea cere un motiv de minimum 10 caractere (spațiile de la capete nu contează).</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("          ", false)]
    [InlineData("123456789", false)]
    [InlineData("   123456789   ", false)]
    [InlineData("1234567890", true)]
    [InlineData("Prea multe absente", true)]
    public void Rejection_reason_needs_at_least_ten_characters(string? reason, bool valid)
    {
        Assert.Equal(valid, ApiInput.HasValidRejectionReason(reason?.Trim()));
    }
}
