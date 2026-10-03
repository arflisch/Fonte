using Fonte.Core.Models;

namespace Fonte.Core.Training;

public enum ProgressionKind
{
    /// <summary>The load to use next time goes up.</summary>
    Weight,

    /// <summary>One more repetition next time (bodyweight exercises).</summary>
    Reps,

    /// <summary>A longer hold next time (timed exercises).</summary>
    Seconds,
}

/// <summary>A target of a template raised after a successful workout.</summary>
public sealed record ProgressionStep(Exercise Exercise, ProgressionKind Kind, double From, double To);

/// <summary>
/// Automatic progression of templates: when every planned set reached its repetitions at the planned load, the
/// next workout aims a little higher.
/// </summary>
public static class Progression
{
    /// <summary>Seconds added to a timed exercise once every set was held long enough.</summary>
    public const int SecondsStep = 5;

    /// <summary>Kilograms added to the load, by equipment (0 when the exercise is not loaded).</summary>
    public static double Increment(Equipment equipment) => equipment switch
    {
        Equipment.Barbell or Equipment.Machine or Equipment.Cable => 2.5,
        Equipment.Dumbbell => 2,
        Equipment.Kettlebell => 4,
        _ => 0,
    };

    /// <summary>
    /// Updates <paramref name="target"/> from the sets done in a workout and returns the raise, if any. Fewer sets
    /// than planned never raise the target. A target load still unknown (0) takes the load that was used.
    /// </summary>
    public static ProgressionStep? Apply(Exercise exercise, TemplateExercise target, IReadOnlyList<WorkoutSet> done)
    {
        if (done.Count == 0 || done.Count < target.Sets)
            return null;

        switch (exercise.Tracking)
        {
            case Tracking.Time:
                if (target.Seconds <= 0)
                {
                    target.Seconds = done.Max(s => s.Seconds);
                    return null;
                }
                if (done.All(s => s.Seconds >= target.Seconds))
                    return Raise(exercise, ProgressionKind.Seconds, target.Seconds, target.Seconds += SecondsStep);
                return null;

            case Tracking.WeightAndReps when Increment(exercise.Equipment) > 0:
                var used = done.Max(s => s.Weight);
                if (target.Weight <= 0)
                {
                    target.Weight = used;
                    return null;
                }
                if (done.All(s => s.Reps >= target.Reps && s.Weight >= target.Weight))
                {
                    var from = target.Weight;
                    target.Weight = Math.Round(from + Increment(exercise.Equipment), 2);
                    return new ProgressionStep(exercise, ProgressionKind.Weight, from, target.Weight);
                }
                return null;

            default:
                if (target.Reps > 0 && done.All(s => s.Reps >= target.Reps))
                    return Raise(exercise, ProgressionKind.Reps, target.Reps, target.Reps += 1);
                return null;
        }
    }

    private static ProgressionStep Raise(Exercise exercise, ProgressionKind kind, double from, double to) =>
        new(exercise, kind, from, to);
}
