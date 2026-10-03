namespace Fonte.Core.Training;

/// <param name="PerSide">Plates to put on each side of the bar, heaviest first.</param>
/// <param name="Achieved">Total weight on the bar with those plates.</param>
public sealed record PlateLoad(IReadOnlyList<double> PerSide, double Achieved)
{
    public double Missing(double target) => Math.Max(0, Math.Round(target - Achieved, 2));
}

/// <summary>A warm-up set before the working sets.</summary>
public sealed record WarmUpSet(double Weight, int Reps);

/// <summary>Which plates to load on a barbell, and how to warm up to the working weight.</summary>
public static class Plates
{
    /// <summary>Plates found in most gyms, in kilograms.</summary>
    public static readonly IReadOnlyList<double> Standard = [25, 20, 15, 10, 5, 2.5, 1.25];

    public static readonly IReadOnlyList<double> Bars = [20, 15, 10];

    /// <summary>Heaviest plates first, as many pairs as needed; never more than the target.</summary>
    public static PlateLoad Load(double target, double bar, IReadOnlyList<double>? plates = null)
    {
        var available = (plates ?? Standard).OrderByDescending(p => p).ToList();
        var perSide = new List<double>();
        var remaining = Math.Round((target - bar) / 2, 3);
        foreach (var plate in available)
        {
            while (remaining + 0.0001 >= plate)
            {
                perSide.Add(plate);
                remaining = Math.Round(remaining - plate, 3);
            }
        }
        return new PlateLoad(perSide, Math.Round(bar + 2 * perSide.Sum(), 2));
    }

    /// <summary>
    /// Sets to do before the working weight: the empty bar, then about 40, 60 and 80 % of it, each rounded down
    /// to a load the plates can make. Light working weights need fewer steps.
    /// </summary>
    public static IReadOnlyList<WarmUpSet> WarmUp(double working, double bar, IReadOnlyList<double>? plates = null)
    {
        if (working <= bar)
            return [];

        var sets = new List<WarmUpSet> { new(bar, 10) };
        foreach (var (share, reps) in new[] { (0.4, 8), (0.6, 5), (0.8, 3) })
        {
            var weight = Load(working * share, bar, plates).Achieved;
            if (weight > sets[^1].Weight && weight < working)
                sets.Add(new WarmUpSet(weight, reps));
        }
        return sets;
    }
}
