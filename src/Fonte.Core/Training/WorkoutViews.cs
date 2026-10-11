using Fonte.Core.Models;

namespace Fonte.Core.Training;

/// <summary>An exercise of a workout with its sets, and the sets done the previous time.</summary>
public sealed record WorkoutEntry(
    WorkoutExercise Item,
    Exercise Exercise,
    IReadOnlyList<WorkoutSet> Sets,
    IReadOnlyList<WorkoutSet> PreviousSets);

public sealed record WorkoutDetail(Workout Workout, IReadOnlyList<WorkoutEntry> Entries);

/// <summary>A set that beats everything done before on that exercise.</summary>
public sealed record PersonalRecord(Exercise Exercise, WorkoutSet Set, double PreviousBest);

public sealed record ExerciseRecap(Exercise Exercise, IReadOnlyList<WorkoutSet> Sets);

public sealed record WorkoutSummary(
    Workout Workout,
    IReadOnlyList<ExerciseRecap> Exercises,
    IReadOnlyList<PersonalRecord> Records)
{
    public TimeSpan Duration => Workout.Duration;

    /// <summary>Working sets (warm-ups left out).</summary>
    public int SetCount => Exercises.Sum(e => Performance.Working(e.Sets).Count());

    public double Volume => Performance.Volume(Exercises.SelectMany(e => e.Sets));

    /// <summary>Name of the template it was started from, if it still exists.</summary>
    public string? TemplateName { get; init; }

    /// <summary>The previous workout of the same template (or the previous one at all, for a free workout).</summary>
    public WorkoutComparison? Previous { get; init; }

    /// <summary>Template targets this workout succeeded (only when it has just been finished).</summary>
    public IReadOnlyList<ProgressionStep> Progressions { get; init; } = [];

    /// <summary>
    /// Whether <see cref="Progressions"/> were saved to the template, or only worked out (automatic progression
    /// is part of Fonte Pro).
    /// </summary>
    public bool ProgressionsApplied { get; init; }
}

public sealed record WorkoutComparison(Workout Workout, double Volume, int SetCount)
{
    public TimeSpan Duration => Workout.Duration;
}

/// <summary>A template with what it contains, for lists.</summary>
public sealed record TemplateOverview(WorkoutTemplate Template, IReadOnlyList<TemplateEntry> Entries)
{
    public int SetCount => Entries.Sum(e => e.Item.Sets);

    /// <summary>When a workout of this template was last finished.</summary>
    public DateTime? LastDone { get; init; }

    /// <summary>The muscle groups it works, most sets first.</summary>
    public IReadOnlyList<MuscleGroup> Muscles =>
        Entries.GroupBy(e => e.Exercise.Muscle).OrderByDescending(g => g.Sum(e => e.Item.Sets)).Select(g => g.Key).ToList();
}

public sealed record TemplateEntry(TemplateExercise Item, Exercise Exercise);

/// <param name="Week">Current week of the program, from 1 (capped at its length).</param>
/// <param name="Next">The template to do next: the one after the last done, in order.</param>
public sealed record ProgramOverview(TrainingProgram Program, IReadOnlyList<TemplateOverview> Templates, int Week, TemplateOverview? Next);

/// <summary>Finished workouts of a calendar month.</summary>
public sealed record MonthReport(int Year, int Month, IReadOnlyList<WorkoutSummary> Workouts)
{
    public IReadOnlySet<DateTime> Days => Workouts.Select(w => w.Workout.StartedAt.Date).ToHashSet();

    public TimeSpan Duration => TimeSpan.FromTicks(Workouts.Sum(w => w.Duration.Ticks));

    public double Volume => Workouts.Sum(w => w.Volume);

    public int SetCount => Workouts.Sum(w => w.SetCount);
}

public sealed record WeekStat(DateTime Start, int Workouts, int Sets, double Volume);

public sealed record DatedRecord(PersonalRecord Record, DateTime Date);

/// <param name="Weeks">The last weeks, oldest first.</param>
/// <param name="MuscleSets">Sets done per muscle group over the last seven days.</param>
/// <param name="Neglected">Muscle groups left out for two weeks while the others were trained.</param>
/// <param name="MonthRecords">Personal records set this month, most recent first.</param>
public sealed record ProgressReport(
    IReadOnlyList<WeekStat> Weeks,
    IReadOnlyDictionary<MuscleGroup, int> MuscleSets,
    IReadOnlyList<MuscleGroup> Neglected,
    IReadOnlyList<DatedRecord> MonthRecords);

/// <summary>How an exercise went in one past workout.</summary>
public sealed record ExerciseSession(Workout Workout, IReadOnlyList<WorkoutSet> Sets);

/// <param name="Sessions">Most recent first.</param>
/// <param name="Best">Best set ever, as scored by <see cref="Performance.Score"/>.</param>
public sealed record ExerciseHistory(Exercise Exercise, IReadOnlyList<ExerciseSession> Sessions, WorkoutSet? Best)
{
    public double BestEstimatedOneRepMax =>
        Sessions.SelectMany(s => s.Sets).Select(s => Performance.EstimatedOneRepMax(s.Weight, s.Reps)).DefaultIfEmpty(0).Max();
}

/// <summary>What the library shows for an exercise: its best set and when it was last done.</summary>
public sealed record ExerciseOverview(Exercise Exercise, WorkoutSet? Best, DateTime? LastDone, int TimesDone);
