using System.Globalization;
using Fonte.Core.Models;

namespace Fonte.Localization;

/// <summary>Short-hands to read translated texts, and to format loads, durations and dates, from code.</summary>
public static class Loc
{
    private static Localizer L => Localizer.Instance;

    public static CultureInfo Culture => L.Culture;

    public static string Get(string key) => L[key];

    public static string Format(string key, params object?[] args) => string.Format(L.Culture, L[key], args);

    /// <summary>Translated message for a refused operation (keys "Error_" + <see cref="FonteError"/> name).</summary>
    public static string Error(FonteException exception) => L[$"Error_{exception.Error}"];

    /// <summary>"1 workout", "3 workouts"… from the "{noun}_One" / "{noun}_Many" keys.</summary>
    public static string Count(int count, string noun) => $"{count} {Noun(count, noun)}";

    public static string Noun(int count, string noun) => L[$"{noun}_{(IsSingular(count) ? "One" : "Many")}"];

    /// <summary>Built-in exercises are translated ("Ex_" + catalog key); the user's own keep their name.</summary>
    public static string ExerciseName(Exercise exercise) => exercise.CustomName ?? L[$"Ex_{exercise.CatalogKey}"];

    public static string Muscle(MuscleGroup muscle) => L[$"Muscle_{muscle}"];

    public static string Equipment(Equipment equipment) => L[$"Equipment_{equipment}"];

    public static string Tracking(Tracking tracking) => L[$"Tracking_{tracking}"];

    /// <summary>"62.5" or "62,5" depending on the language, without useless decimals.</summary>
    public static string Number(double value) => value.ToString("0.##", L.Culture);

    public static string Weight(double kilograms) => $"{Number(kilograms)} kg";

    /// <summary>Total load lifted, "12,450 kg".</summary>
    public static string Volume(double kilograms) => $"{kilograms.ToString("N0", L.Culture)} kg";

    /// <summary>A set as it reads in a history: "60 kg × 10", "12 reps", "1 min 30".</summary>
    public static string Set(Exercise exercise, WorkoutSet set) => exercise.Tracking switch
    {
        Core.Models.Tracking.Time => Seconds(set.Seconds),
        Core.Models.Tracking.WeightAndReps when set.Weight > 0 => $"{Weight(set.Weight)} × {set.Reps}",
        _ => $"{set.Reps} {L["Unit_Reps"]}",
    };

    /// <summary>A set in the narrow "previous" column of a workout: "60 × 10", "12", "1 min 30".</summary>
    public static string SetShort(Exercise exercise, WorkoutSet set) => exercise.Tracking switch
    {
        Core.Models.Tracking.Time => Seconds(set.Seconds),
        Core.Models.Tracking.WeightAndReps when set.Weight > 0 => $"{Number(set.Weight)} × {set.Reps}",
        _ => set.Reps.ToString(L.Culture),
    };

    /// <summary>A duration counted in seconds: "45 s", "2 min", "1 min 30", "1 h 05".</summary>
    public static string Seconds(int seconds) => seconds switch
    {
        < 60 => $"{seconds} s",
        >= 3600 => $"{seconds / 3600} h {seconds % 3600 / 60:00}",
        _ when seconds % 60 == 0 => $"{seconds / 60} min",
        _ => $"{seconds / 60} min {seconds % 60:00}",
    };

    /// <summary>How long a workout lasted: "52 min", "1 h 05".</summary>
    public static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{(int)duration.TotalHours} h {duration.Minutes:00}" : $"{Math.Max(1, (int)duration.TotalMinutes)} min";

    /// <summary>A running clock: "4:07", "1:02:03".</summary>
    public static string Clock(TimeSpan time)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(time.TotalSeconds - 0.001));
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds % 3600 / 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>"Today", "Yesterday", "Monday 28 September", with the year when it is not this one.</summary>
    public static string Day(DateTime date)
    {
        var today = DateTime.Today;
        if (date.Date == today)
            return L["Date_Today"];
        if (date.Date == today.AddDays(-1))
            return L["Date_Yesterday"];
        return Date(date, date.Year == today.Year ? "dddd d MMMM" : "d MMMM yyyy");
    }

    /// <summary>Formats a date with the current language, capitalising the first letter ("Thursday 24 September").</summary>
    public static string Date(DateTime date, string format) => Capitalize(date.ToString(format, L.Culture));

    public static string Time(DateTime date) => date.ToString("t", L.Culture);

    public static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], L.Culture) + text[1..];

    // French treats 0 as singular ("0 séance"), English and Dutch do not.
    private static bool IsSingular(int count) => count == 1 || (count == 0 && L.Language.Code == "fr");
}
