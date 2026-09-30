using Fonte.Core.Catalog;
using Fonte.Core.Models;
using Fonte.Core.Training;

namespace Fonte.Core.Tests;

public sealed class PerformanceTests
{
    [Theory]
    [InlineData(100, 1, 100)]
    [InlineData(100, 5, 116.67)]
    [InlineData(80, 10, 106.67)]
    [InlineData(60, 20, 84)] // capped at 12 repetitions
    [InlineData(100, 0, 0)]
    [InlineData(0, 8, 0)]
    public void Estimated_one_rep_max_uses_epley(double weight, int reps, double expected)
    {
        Assert.Equal(expected, Math.Round(Performance.EstimatedOneRepMax(weight, reps), 2));
    }

    [Fact]
    public void Sets_are_compared_by_what_the_exercise_measures()
    {
        var heavy = new WorkoutSet { Weight = 100, Reps = 3 }; // ≈ 110 kg
        var light = new WorkoutSet { Weight = 90, Reps = 8 }; // ≈ 114 kg

        Assert.True(Performance.Score(Tracking.WeightAndReps, heavy) < Performance.Score(Tracking.WeightAndReps, light));
        Assert.Equal(12, Performance.Score(Tracking.Reps, new WorkoutSet { Reps = 12, Weight = 20 }));
        Assert.Equal(45, Performance.Score(Tracking.Time, new WorkoutSet { Seconds = 45 }));
        Assert.Same(light, Performance.Best(Tracking.WeightAndReps, [heavy, light]));
    }

    [Fact]
    public void Volume_is_load_times_repetitions()
    {
        Assert.Equal(1192.5, Performance.Volume([new WorkoutSet { Weight = 80, Reps = 6 }, new WorkoutSet { Weight = 82.5, Reps = 5 }, new WorkoutSet { Weight = 60, Reps = 5 }]));
    }

    [Fact]
    public void Catalog_keys_are_unique_and_every_muscle_group_has_exercises()
    {
        var keys = ExerciseCatalog.All.Select(e => e.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.Matches("^[a-z_]+$", k));
        Assert.All(Enum.GetValues<MuscleGroup>(), m => Assert.Contains(ExerciseCatalog.All, e => e.Muscle == m));
    }
}
