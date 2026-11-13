using LeaveRemote.Api.Services;

namespace LeaveRemote.Api.Tests.Unit;

/// <summary>Cerința 8: zilele lucrătoare exclud weekendurile și sărbătorile legale configurate.</summary>
public class WorkingDaysTests
{
    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    [Fact]
    public void Weekend_days_are_excluded()
    {
        var days = WorkingDays.Between(D(11, 13), D(11, 16), []);

        Assert.Equal([D(11, 13), D(11, 16)], days);
    }

    [Fact]
    public void Configured_public_holidays_are_excluded()
    {
        var days = WorkingDays.Between(D(11, 27), D(12, 2), [D(11, 30), D(12, 1)]);

        Assert.Equal([D(11, 27), D(12, 2)], days);
    }

    [Fact]
    public void An_interval_with_only_weekend_days_has_no_working_days()
    {
        Assert.Empty(WorkingDays.Between(D(11, 14), D(11, 15), []));
    }

    [Fact]
    public void A_single_day_interval_is_counted_once()
    {
        Assert.Equal([D(11, 18)], WorkingDays.Between(D(11, 18), D(11, 18), []));
    }

    [Fact]
    public void Intervals_crossing_the_year_use_the_holidays_of_both_years()
    {
        // 28–31 dec 2026 (4 zile), 1–2 ian sărbători, 3 ian duminică, 4–5 ian, 6–7 ian sărbători, 8 ian.
        var holidays = new[] { D(12, 25), D(12, 26), D(1, 1, 2027), D(1, 2, 2027), D(1, 6, 2027), D(1, 7, 2027) };

        var days = WorkingDays.Between(D(12, 28), D(1, 8, 2027), holidays);

        Assert.Equal(7, days.Count);
        Assert.Equal(4, days.Count(d => d.Year == 2026));
    }

    [Theory]
    [InlineData(2026, 11, 14, true)]
    [InlineData(2026, 11, 15, true)]
    [InlineData(2026, 11, 16, false)]
    [InlineData(2026, 11, 13, false)]
    public void Saturday_and_Sunday_are_weekend(int year, int month, int day, bool expected)
    {
        Assert.Equal(expected, WorkingDays.IsWeekend(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(18, 16)]
    [InlineData(22, 16)]
    [InlineData(23, 23)]
    public void Calendar_week_starts_on_monday_and_ends_on_sunday(int day, int expectedMonday)
    {
        Assert.Equal(D(11, expectedMonday), WorkingDays.MondayOf(D(11, day)));
    }
}
