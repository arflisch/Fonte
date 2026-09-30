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

    public int SetCount => Exercises.Sum(e => e.Sets.Count);

    public double Volume => Performance.Volume(Exercises.SelectMany(e => e.Sets));
}

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
