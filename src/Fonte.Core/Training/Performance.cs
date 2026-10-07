using Fonte.Core.Models;

namespace Fonte.Core.Training;

public static class Performance
{
    /// <summary>
    /// Estimated one-rep max (Epley): the load that could be lifted once. 100 kg × 5 ≈ 116.7 kg.
    /// Beyond 12 repetitions the estimate is unreliable, so it is capped there.
    /// </summary>
    public static double EstimatedOneRepMax(double weight, int reps) => reps switch
    {
        _ when weight <= 0 || reps <= 0 => 0,
        1 => weight,
        _ => weight * (1 + Math.Min(reps, 12) / 30.0),
    };

    /// <summary>
    /// Compares sets of the same exercise: the estimated one-rep max for loaded sets, otherwise the repetitions,
    /// or the duration for timed exercises. Higher is better.
    /// </summary>
    public static double Score(Tracking tracking, WorkoutSet set) => tracking switch
    {
        Tracking.Time => set.Seconds,
        Tracking.Reps => set.Reps,
        _ when set.Weight > 0 => EstimatedOneRepMax(set.Weight, set.Reps),
        _ => set.Reps,
    };

    /// <summary>Warm-up sets prepare the work: they never count in volume, set counts or records.</summary>
    public static bool IsWorking(WorkoutSet set) => set.Kind != SetKind.WarmUp;

    /// <summary>The sets a template's targets are about: drop and warm-up sets are extras.</summary>
    public static bool IsPlanned(WorkoutSet set) => set.Kind is SetKind.Normal or SetKind.Failure;

    public static IEnumerable<WorkoutSet> Working(IEnumerable<WorkoutSet> sets) => sets.Where(IsWorking);

    /// <summary>Kilograms lifted: load × repetitions, summed over loaded working sets.</summary>
    public static double Volume(IEnumerable<WorkoutSet> sets) => Working(sets).Sum(s => s.Weight * s.Reps);

    /// <summary>The working set that scores highest, or null when there is none.</summary>
    public static WorkoutSet? Best(Tracking tracking, IEnumerable<WorkoutSet> sets) =>
        Working(sets).OrderByDescending(s => Score(tracking, s)).ThenByDescending(s => s.Weight).FirstOrDefault();
}
