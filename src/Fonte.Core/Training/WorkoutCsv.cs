using System.Globalization;
using System.Text;
using Fonte.Core.Models;

namespace Fonte.Core.Training;

/// <summary>Column titles and names of the CSV export, in the user's language.</summary>
public sealed record CsvTexts(
    string Date, string Time, string Workout, string Exercise, string Muscle, string Set, string Kind,
    string Weight, string Reps, string Seconds, string Rpe, string Note,
    Func<Exercise, string> ExerciseName, Func<MuscleGroup, string> MuscleName, Func<SetKind, string> KindName);

/// <summary>
/// Writes every set of every finished workout as a spreadsheet-friendly CSV, one line per set: the culture's
/// list separator and decimal comma, so Excel or Numbers open it directly, and a BOM so accents survive.
/// </summary>
public static class WorkoutCsv
{
    /// <param name="workouts">In any order: they are written oldest first.</param>
    public static byte[] Write(IEnumerable<WorkoutSummary> workouts, CsvTexts texts, CultureInfo culture)
    {
        var separator = culture.TextInfo.ListSeparator is { Length: > 0 } s ? s : ",";
        var csv = new StringBuilder();
        void Row(params string?[] cells) =>
            csv.Append(string.Join(separator, cells.Select(c => Escape(c ?? string.Empty, separator)))).Append("\r\n");

        Row(texts.Date, texts.Time, texts.Workout, texts.Exercise, texts.Muscle, texts.Set, texts.Kind,
            texts.Weight, texts.Reps, texts.Seconds, texts.Rpe, texts.Note);
        foreach (var summary in workouts.OrderBy(w => w.Workout.StartedAt))
        {
            var workout = summary.Workout;
            var note = workout.Note;
            foreach (var recap in summary.Exercises)
            {
                var number = 0;
                foreach (var set in recap.Sets.OrderBy(s => s.Position))
                {
                    var tracking = recap.Exercise.Tracking;
                    Row(
                        workout.StartedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        workout.StartedAt.ToString("HH:mm", CultureInfo.InvariantCulture),
                        summary.TemplateName,
                        texts.ExerciseName(recap.Exercise),
                        texts.MuscleName(recap.Exercise.Muscle),
                        set.Kind == SetKind.WarmUp ? string.Empty : (++number).ToString(culture),
                        texts.KindName(set.Kind),
                        tracking == Tracking.WeightAndReps && set.Weight > 0 ? set.Weight.ToString("0.##", culture) : null,
                        tracking == Tracking.Time ? null : set.Reps.ToString(culture),
                        tracking == Tracking.Time ? set.Seconds.ToString(culture) : null,
                        set.Rpe?.ToString("0.#", culture),
                        note);
                    // The note belongs to the workout: written once, on its first line.
                    note = null;
                }
            }
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Escape(string cell, string separator) =>
        cell.Contains(separator, StringComparison.Ordinal) || cell.IndexOfAny(['"', '\r', '\n']) >= 0
            ? $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : cell;
}
