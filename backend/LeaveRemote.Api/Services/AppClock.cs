namespace LeaveRemote.Api.Services;

/// <summary>
/// Sursa unică pentru „azi”. Toate regulile lucrează cu date calendaristice (fără oră),
/// calculate în ora României, indiferent de fusul orar al serverului.
/// Bazat pe <see cref="TimeProvider"/>, deci testele pot fixa ziua curentă.
/// </summary>
public sealed class AppClock(TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo RomaniaTimeZone = ResolveRomaniaTimeZone();

    public DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), RomaniaTimeZone).DateTime);

    public int CurrentYear => Today.Year;

    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private static TimeZoneInfo ResolveRomaniaTimeZone()
    {
        foreach (var id in new[] { "Europe/Bucharest", "GTB Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }
        return TimeZoneInfo.Utc;
    }
}
