namespace Fonte.Core.Training;

public static class Streaks
{
    /// <summary>The week (from <paramref name="firstDay"/>) that contains <paramref name="date"/>.</summary>
    public static DateTime WeekStart(DateTime date, DayOfWeek firstDay) =>
        date.Date.AddDays(-(((int)date.DayOfWeek - (int)firstDay + 7) % 7));

    /// <summary>
    /// Consecutive weeks, up to now, with at least <paramref name="goal"/> workouts. The current week counts once
    /// its goal is met; until then it doesn't break the series.
    /// </summary>
    public static int RegularWeeks(IEnumerable<DateTime> workoutDates, int goal, DateTime today, DayOfWeek firstDay)
    {
        if (goal <= 0)
            return 0;

        var perWeek = workoutDates
            .GroupBy(d => WeekStart(d, firstDay))
            .ToDictionary(g => g.Key, g => g.Count());
        var week = WeekStart(today, firstDay);
        var count = perWeek.GetValueOrDefault(week) >= goal ? 1 : 0;
        for (week = week.AddDays(-7); perWeek.GetValueOrDefault(week) >= goal; week = week.AddDays(-7))
            count++;
        return count;
    }
}
