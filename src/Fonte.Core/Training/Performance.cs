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

    /// <summary>Kilograms lifted: load × repetitions, summed over loaded sets.</summary>
    public static double Volume(IEnumerable<WorkoutSet> sets) => sets.Sum(s => s.Weight * s.Reps);

    /// <summary>The set that scores highest, or null when there is none.</summary>
    public static WorkoutSet? Best(Tracking tracking, IEnumerable<WorkoutSet> sets) =>
        sets.OrderByDescending(s => Score(tracking, s)).ThenByDescending(s => s.Weight).FirstOrDefault();
}
