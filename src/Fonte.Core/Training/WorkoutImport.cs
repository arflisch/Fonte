using System.Globalization;
using System.Text;
using Fonte.Core.Catalog;
using Fonte.Core.Models;

namespace Fonte.Core.Training;

/// <summary>The app a history comes from.</summary>
public enum ImportSource
{
    Strong,
    Hevy,
}

/// <summary>A set of an imported workout; the load in kilograms.</summary>
public sealed record ImportedSet(double Weight, int Reps, int Seconds, SetKind Kind, double? Rpe);

/// <param name="SupersetId">Hevy's superset number: consecutive exercises sharing it are done back to back.</param>
public sealed record ImportedExercise(string Name, IReadOnlyList<ImportedSet> Sets, string? SupersetId = null);

public sealed record ImportedWorkout(
    DateTime StartedAt, DateTime FinishedAt, string? Title, string? Note, IReadOnlyList<ImportedExercise> Exercises);

/// <param name="WeightsInPounds">Whether the loads were read as pounds (from the file, or as the user said).</param>
/// <param name="UnitKnown">The file says its unit (Hevy, recent Strong); otherwise the user is asked.</param>
public sealed record ImportFile(ImportSource Source, IReadOnlyList<ImportedWorkout> Workouts, bool WeightsInPounds, bool UnitKnown);

/// <summary>An exercise of another app and the one it becomes in Fonte.</summary>
/// <param name="CatalogKey">A built-in exercise; null for one created on import (or one of the user's own).</param>
public sealed record ImportedExerciseMatch(string Name, string? CatalogKey, MuscleGroup Muscle, Equipment Equipment, Tracking Tracking);

/// <summary>
/// Reads the CSV exports of Strong ("Export Data") and Hevy ("Export Workouts"): one line per set. The header
/// says which app wrote the file; columns are found by name, so their order and the separator don't matter.
/// </summary>
public static class WorkoutImport
{
    private static readonly string[] HevyDates = ["d MMM yyyy, HH:mm", "dd MMM yyyy, HH:mm", "d MMM yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm"];

    private static readonly string[] StrongDates = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss", "M/d/yyyy H:mm", "d/M/yyyy H:mm"];

    /// <summary>Reads a file; null when it is not an export of Strong or Hevy.</summary>
    /// <param name="pounds">Unit of the loads when the file doesn't say it (older Strong exports).</param>
    public static ImportFile? Read(string text, bool pounds)
    {
        var rows = ParseCsv(text);
        if (rows.Count < 2)
            return null;
        var header = rows[0].Select(Key).ToList();
        int Col(params string[] names) => names.Select(n => header.IndexOf(Key(n))).FirstOrDefault(i => i >= 0, -1);

        if (Col("exercise_title") >= 0)
            return ReadHevy(rows, Col);
        if (Col("Exercise Name") >= 0 && Col("Date") >= 0)
            return ReadStrong(rows, Col, pounds);
        return null;
    }

    private static ImportFile ReadHevy(List<string[]> rows, Func<string[], int> col)
    {
        int title = col(["title"]), start = col(["start_time"]), end = col(["end_time"]), description = col(["description"]);
        int exercise = col(["exercise_title"]), superset = col(["superset_id"]), type = col(["set_type"]);
        int reps = col(["reps"]), seconds = col(["duration_seconds"]), rpe = col(["rpe"]);
        int kg = col(["weight_kg"]), lbs = col(["weight_lbs"]);
        var pounds = kg < 0 && lbs >= 0;

        var workouts = new List<ImportedWorkout>();
        foreach (var group in rows.Skip(1).Where(r => Cell(r, exercise).Length > 0).GroupBy(r => (Cell(r, start), Cell(r, title))))
        {
            if (Date(Cell(group.First(), start), HevyDates) is not { } started)
                continue;
            var finished = Date(Cell(group.First(), end), HevyDates) is { } e && e > started ? e : started.AddHours(1);
            var exercises = Exercises(group, r => Cell(r, exercise), r => Cell(r, superset), r => Set(
                Number(Cell(r, pounds ? lbs : kg)), pounds, Number(Cell(r, reps)), Number(Cell(r, seconds)),
                Cell(r, type) switch
                {
                    "warmup" => SetKind.WarmUp,
                    "dropset" => SetKind.Drop,
                    "failure" => SetKind.Failure,
                    _ => SetKind.Normal,
                },
                Number(Cell(r, rpe))));
            workouts.Add(new ImportedWorkout(started, finished, Text(Cell(group.First(), title)), Text(Cell(group.First(), description)), exercises));
        }
        return new ImportFile(ImportSource.Hevy, workouts, pounds, UnitKnown: true);
    }

    private static ImportFile ReadStrong(List<string[]> rows, Func<string[], int> col, bool poundsByDefault)
    {
        int date = col(["Date"]), name = col(["Workout Name"]), duration = col(["Duration", "Duration (sec)"]);
        int exercise = col(["Exercise Name"]), order = col(["Set Order"]), reps = col(["Reps"]);
        int seconds = col(["Seconds"]), rpe = col(["RPE"]), notes = col(["Workout Notes"]);
        int plain = col(["Weight"]), kg = col(["Weight (kg)"]), lbs = col(["Weight (lbs)", "Weight (lb)"]);
        var unitKnown = kg >= 0 || lbs >= 0;
        var pounds = unitKnown ? kg < 0 : poundsByDefault;
        var weight = kg >= 0 ? kg : lbs >= 0 ? lbs : plain;
        var durationInSeconds = rows[0].ElementAtOrDefault(duration)?.Contains("sec", StringComparison.OrdinalIgnoreCase) == true;

        var workouts = new List<ImportedWorkout>();
        // Recent exports add a line per rest timer: they have no set order of their own.
        var sets = rows.Skip(1).Where(r => Cell(r, exercise).Length > 0 && !Cell(r, order).StartsWith("Rest", StringComparison.OrdinalIgnoreCase));
        foreach (var group in sets.GroupBy(r => (Cell(r, date), Cell(r, name))))
        {
            if (Date(Cell(group.First(), date), StrongDates) is not { } started)
                continue;
            var length = durationInSeconds ? TimeSpan.FromSeconds(Number(Cell(group.First(), duration))) : Duration(Cell(group.First(), duration));
            var exercises = Exercises(group, r => Cell(r, exercise), _ => null, r => Set(
                Number(Cell(r, weight)), pounds, Number(Cell(r, reps)), Number(Cell(r, seconds)),
                Cell(r, order).ToUpperInvariant() switch
                {
                    "W" => SetKind.WarmUp,
                    "D" => SetKind.Drop,
                    "F" => SetKind.Failure,
                    _ => SetKind.Normal,
                },
                Number(Cell(r, rpe))));
            workouts.Add(new ImportedWorkout(started, started + (length > TimeSpan.Zero ? length : TimeSpan.FromHours(1)),
                Text(Cell(group.First(), name)), Text(Cell(group.First(), notes)), exercises));
        }
        return new ImportFile(ImportSource.Strong, workouts, pounds, unitKnown);
    }

    /// <summary>The exercises of a workout in the order they were done, each with its sets.</summary>
    private static List<ImportedExercise> Exercises(
        IEnumerable<string[]> rows, Func<string[], string> name, Func<string[], string?> superset, Func<string[], ImportedSet?> set)
    {
        var exercises = new List<(string Name, string? Superset, List<ImportedSet> Sets)>();
        foreach (var row in rows)
        {
            // A new block starts when the exercise changes: the same exercise done twice stays twice.
            if (exercises.Count == 0 || exercises[^1].Name != name(row))
                exercises.Add((name(row), superset(row) is { Length: > 0 } s ? s : null, []));
            if (set(row) is { } done)
                exercises[^1].Sets.Add(done);
        }
        return exercises.Where(e => e.Sets.Count > 0).Select(e => new ImportedExercise(e.Name, e.Sets, e.Superset)).ToList();
    }

    private static ImportedSet? Set(double weight, bool pounds, double reps, double seconds, SetKind kind, double rpe)
    {
        if (weight <= 0 && reps <= 0 && seconds <= 0)
            return null;
        var kilograms = pounds ? Units.ToKilograms(weight) : weight;
        return new ImportedSet(Math.Round(Math.Max(0, kilograms), 3), (int)Math.Round(Math.Max(0, reps)), (int)Math.Round(Math.Max(0, seconds)),
            kind, rpe is >= 1 and <= 10 ? rpe : null);
    }

    /// <summary>
    /// What each exercise of the file becomes: a built-in exercise when the name is one Strong or Hevy give it, the
    /// user's own exercise of the same name, or else a new exercise whose muscle and equipment are guessed.
    /// </summary>
    public static IReadOnlyList<ImportedExerciseMatch> Match(IEnumerable<ImportedWorkout> workouts)
    {
        var sets = workouts.SelectMany(w => w.Exercises).GroupBy(e => e.Name).ToList();
        return sets.Select(g => Match(g.Key, g.SelectMany(e => e.Sets).ToList())).ToList();
    }

    private static ImportedExerciseMatch Match(string name, IReadOnlyList<ImportedSet> sets)
    {
        var normalized = Normalize(name);
        var (baseName, equipmentText) = Split(normalized);
        var equipment = EquipmentOf(equipmentText) ?? EquipmentOf(baseName);
        var known = Aliases.FirstOrDefault(a => a.Names.Contains(normalized))
            ?? Aliases.FirstOrDefault(a => a.Names.Contains(baseName)
                && (equipment is null || Catalog(a.Key).Equipment == equipment));
        if (known is not null)
        {
            var catalog = Catalog(known.Key);
            return new ImportedExerciseMatch(name, catalog.Key, catalog.Muscle, catalog.Equipment, catalog.Tracking);
        }

        var tracking = sets.All(s => s.Weight <= 0 && s.Reps <= 0) ? Tracking.Time
            : sets.All(s => s.Weight <= 0) ? Tracking.Reps
            : Tracking.WeightAndReps;
        var muscle = MuscleOf(baseName) ?? (tracking == Tracking.Time ? MuscleGroup.Cardio : MuscleGroup.Core);
        return new ImportedExerciseMatch(name, null, muscle, equipment ?? (tracking == Tracking.WeightAndReps ? Models.Equipment.Other : Models.Equipment.Bodyweight), tracking);
    }

    private static CatalogExercise Catalog(string key) => ExerciseCatalog.All.First(c => c.Key == key);

    private sealed record Alias(string Key, HashSet<string> Names);

    /// <summary>The names Strong and Hevy give the built-in exercises (lower case, without accents or dashes).</summary>
    private static readonly IReadOnlyList<Alias> Aliases =
    [
        A("bench_press", "bench press (barbell)", "bench press", "barbell bench press", "flat bench press"),
        A("incline_bench_press", "incline bench press (barbell)", "incline bench press", "incline barbell bench press"),
        A("dumbbell_bench_press", "bench press (dumbbell)", "dumbbell bench press", "dumbbell press"),
        A("incline_dumbbell_press", "incline bench press (dumbbell)", "incline dumbbell press", "incline dumbbell bench press"),
        A("dumbbell_fly", "chest fly (dumbbell)", "dumbbell fly", "fly (dumbbell)", "dumbbell flyes"),
        A("cable_crossover", "cable crossover", "cable fly crossovers", "cable fly", "chest fly (cable)"),
        A("chest_press_machine", "chest press (machine)", "chest press", "machine chest press"),
        A("push_up", "push up", "pushup", "push ups"),
        A("dips", "chest dip", "dips", "dip", "triceps dip", "chest dip (assisted)"),
        A("deadlift", "deadlift (barbell)", "deadlift", "conventional deadlift"),
        A("barbell_row", "bent over row (barbell)", "barbell row", "bent over row", "pendlay row (barbell)"),
        A("dumbbell_row", "bent over one arm row (dumbbell)", "dumbbell row", "one arm row (dumbbell)", "single arm row (dumbbell)"),
        A("pull_up", "pull up", "pullup", "pull ups", "pull up (assisted)", "wide pull up"),
        A("chin_up", "chin up", "chinup", "chin ups"),
        A("lat_pulldown", "lat pulldown (cable)", "lat pulldown", "lat pulldown (machine)", "pulldown"),
        A("seated_cable_row", "seated row (cable)", "seated cable row", "seated cable row v grip (cable)", "seated row (machine)", "cable row"),
        A("back_extension", "back extension", "back extension (hyperextension)", "hyperextension"),
        A("overhead_press", "overhead press (barbell)", "overhead press", "military press", "strict military press (barbell)", "shoulder press (barbell)", "standing military press (barbell)"),
        A("dumbbell_shoulder_press", "shoulder press (dumbbell)", "dumbbell shoulder press", "seated overhead press (dumbbell)", "overhead press (dumbbell)", "arnold press (dumbbell)"),
        A("lateral_raise", "lateral raise (dumbbell)", "lateral raise", "side lateral raise"),
        A("front_raise", "front raise (dumbbell)", "front raise"),
        A("rear_delt_fly", "reverse fly (dumbbell)", "rear delt fly", "rear delt reverse fly (dumbbell)", "reverse fly"),
        A("face_pull", "face pull (cable)", "face pull"),
        A("shrug", "shrug (dumbbell)", "shrug", "dumbbell shrug"),
        A("barbell_curl", "bicep curl (barbell)", "barbell curl", "biceps curl (barbell)"),
        A("dumbbell_curl", "bicep curl (dumbbell)", "dumbbell curl", "biceps curl (dumbbell)", "bicep curl"),
        A("hammer_curl", "hammer curl (dumbbell)", "hammer curl"),
        A("triceps_pushdown", "triceps pushdown (cable straight bar)", "triceps pushdown", "triceps pushdown (cable)", "tricep pushdown", "triceps rope pushdown", "triceps pushdown (rope)"),
        A("skull_crusher", "skullcrusher (barbell)", "skull crusher", "skullcrusher", "skullcrusher (ez bar)", "lying triceps extension (barbell)"),
        A("overhead_triceps_extension", "triceps extension (dumbbell)", "overhead triceps extension", "overhead triceps extension (dumbbell)", "triceps extension"),
        A("close_grip_bench_press", "bench press close grip (barbell)", "close grip bench press", "close grip bench press (barbell)"),
        A("squat", "squat (barbell)", "squat", "back squat", "barbell squat", "full squat"),
        A("front_squat", "front squat (barbell)", "front squat"),
        A("leg_press", "leg press", "leg press (machine)", "leg press horizontal (machine)"),
        A("romanian_deadlift", "romanian deadlift (barbell)", "romanian deadlift", "rdl"),
        A("lunge", "lunge (dumbbell)", "lunge", "walking lunge (dumbbell)", "dumbbell lunge"),
        A("bulgarian_split_squat", "bulgarian split squat", "bulgarian split squat (dumbbell)", "split squat (dumbbell)"),
        A("leg_extension", "leg extension (machine)", "leg extension"),
        A("leg_curl", "lying leg curl (machine)", "seated leg curl (machine)", "leg curl", "leg curl (machine)"),
        A("hip_thrust", "hip thrust (barbell)", "hip thrust"),
        A("calf_raise", "standing calf raise (machine)", "calf raise", "seated calf raise (machine)", "calf raise (machine)", "calf press on leg press"),
        A("goblet_squat", "goblet squat (kettlebell)", "goblet squat", "goblet squat (dumbbell)"),
        A("plank", "plank", "front plank"),
        A("crunch", "crunch", "crunches"),
        A("hanging_leg_raise", "hanging leg raise", "hanging knee raise"),
        A("cable_crunch", "cable crunch", "cable crunch (cable)"),
        A("ab_wheel", "ab wheel", "ab wheel rollout", "ab roller"),
        A("running", "running", "running (treadmill)", "treadmill", "run", "outdoor running"),
        A("rowing_machine", "rowing (machine)", "rowing machine", "rower", "rowing"),
        A("cycling", "cycling", "cycling (indoor)", "stationary bike", "bike", "spinning"),
        A("jump_rope", "jump rope", "skipping"),
    ];

    private static Alias A(string key, params string[] names) => new(key, names.Select(Normalize).ToHashSet());

    private static (string Base, string Equipment) Split(string name)
    {
        var open = name.IndexOf('(');
        return open < 0 ? (name, string.Empty) : (name[..open].Trim(), name[(open + 1)..].TrimEnd(')').Trim());
    }

    private static Equipment? EquipmentOf(string text) =>
        text.Contains("barbell") || text.Contains("ez bar") ? Models.Equipment.Barbell
        : text.Contains("dumbbell") ? Models.Equipment.Dumbbell
        : text.Contains("kettlebell") ? Models.Equipment.Kettlebell
        : text.Contains("cable") ? Models.Equipment.Cable
        : text.Contains("machine") || text.Contains("smith") ? Models.Equipment.Machine
        : text.Contains("bodyweight") || text.Contains("weighted") ? Models.Equipment.Bodyweight
        : text.Length > 0 && text.Contains("band") ? Models.Equipment.Other
        : null;

    private static MuscleGroup? MuscleOf(string name)
    {
        bool Has(params string[] words) => words.Any(name.Contains);
        return Has("bench", "chest", "fly", "pec", "push up", "dip") ? MuscleGroup.Chest
            : Has("row", "pull", "lat", "deadlift", "chin", "back") ? MuscleGroup.Back
            : Has("shoulder", "press", "raise", "delt", "shrug", "upright") ? MuscleGroup.Shoulders
            : Has("curl", "tricep", "bicep", "skull", "extension", "kickback") ? MuscleGroup.Arms
            : Has("squat", "leg", "lunge", "calf", "glute", "hip", "thrust", "hamstring", "quad", "step") ? MuscleGroup.Legs
            : Has("crunch", "plank", "ab", "sit up", "twist", "core", "oblique") ? MuscleGroup.Core
            : Has("run", "bike", "cycl", "row", "walk", "elliptical", "stair", "swim", "cardio", "jump") ? MuscleGroup.Cardio
            : null;
    }

    /// <summary>Lower case, without accents; dashes and repeated spaces folded.</summary>
    public static string Normalize(string name) =>
        string.Join(' ', TextSearch.Normalize(name).Replace('-', ' ').Replace('–', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Key(string header) => Normalize(header.Trim().Trim('﻿'));

    private static string Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index].Trim() : string.Empty;

    private static string? Text(string value) => value.Length == 0 ? null : value;

    private static double Number(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0;

    private static DateTime? Date(string text, string[] formats) =>
        DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date) ? date
        : DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date) ? date
        : null;

    /// <summary>Strong's "1h 5m", "45m", "50s".</summary>
    private static TimeSpan Duration(string text)
    {
        var total = TimeSpan.Zero;
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var unit = part[^1];
            var value = Number(part[..^1]);
            total += char.ToLowerInvariant(unit) switch
            {
                'h' => TimeSpan.FromHours(value),
                'm' => TimeSpan.FromMinutes(value),
                's' => TimeSpan.FromSeconds(value),
                _ => TimeSpan.Zero,
            };
        }
        return total;
    }

    /// <summary>Splits CSV text into rows: quoted cells, doubled quotes, line breaks inside quotes; "," or ";".</summary>
    public static List<string[]> ParseCsv(string text)
    {
        text = text.TrimStart('﻿');
        var firstLine = text.Split('\n', 2)[0];
        var separator = firstLine.Count(c => c == ';') > firstLine.Count(c => c == ',') ? ';' : ',';

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(cell.ToString());
                cell.Clear();
                if (row.Any(r => r.Length > 0))
                    rows.Add([.. row]);
                row.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }
        row.Add(cell.ToString());
        if (row.Any(r => r.Length > 0))
            rows.Add([.. row]);
        return rows;
    }
}
