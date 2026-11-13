namespace LeaveRemote.Api.Services;

/// <summary>Calcule pe zile calendaristice: weekend, zile lucrătoare, săptămâna luni–duminică.</summary>
public static class WorkingDays
{
    public static bool IsWeekend(DateOnly date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>Zilele din [start, end] care nu sunt în weekend și nici sărbători legale.</summary>
    public static List<DateOnly> Between(DateOnly start, DateOnly end, IEnumerable<DateOnly> holidays)
    {
        var holidaySet = holidays.ToHashSet();
        var dates = new List<DateOnly>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!IsWeekend(date) && !holidaySet.Contains(date)) dates.Add(date);
        }
        return dates;
    }

    /// <summary>Lunea săptămânii calendaristice (luni–duminică) care conține data.</summary>
    public static DateOnly MondayOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
