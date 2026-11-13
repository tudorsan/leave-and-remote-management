using LeaveRemote.Api.Services;

namespace LeaveRemote.Api.Tests.Unit;

/// <summary>Sărbătorile legale din România, inclusiv cele mobile legate de Paștele ortodox.</summary>
public class RomanianPublicHolidaysTests
{
    [Theory]
    [InlineData(2025, 4, 20)]
    [InlineData(2026, 4, 12)]
    [InlineData(2027, 5, 2)]
    [InlineData(2028, 4, 16)]
    [InlineData(2029, 4, 8)]
    public void Orthodox_Easter_falls_on_the_right_date(int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), RomanianPublicHolidays.OrthodoxEaster(year));
    }

    [Fact]
    public void A_year_has_the_fixed_and_the_movable_public_holidays()
    {
        var dates = RomanianPublicHolidays.For(2028).Select(h => h.Date).ToList();

        Assert.Contains(new DateOnly(2028, 1, 24), dates);   // Ziua Unirii
        Assert.Contains(new DateOnly(2028, 4, 14), dates);   // Vinerea Mare
        Assert.Contains(new DateOnly(2028, 4, 17), dates);   // A doua zi de Paște
        Assert.Contains(new DateOnly(2028, 6, 4), dates);    // Rusaliile
        Assert.Contains(new DateOnly(2028, 12, 1), dates);   // Ziua Națională
        Assert.Equal(dates.Count, dates.Distinct().Count());
    }

    [Fact]
    public void Holidays_falling_on_the_same_day_become_one_day_off()
    {
        // În 2026, a doua zi de Rusalii cade pe 1 iunie, odată cu Ziua Copilului.
        var juneFirst = Assert.Single(RomanianPublicHolidays.For(2026), h => h.Date == new DateOnly(2026, 6, 1));

        Assert.Contains("Ziua Copilului", juneFirst.Name);
        Assert.Contains("A doua zi de Rusalii", juneFirst.Name);
    }
}
