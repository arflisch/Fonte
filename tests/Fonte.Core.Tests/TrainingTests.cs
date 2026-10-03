using Fonte.Core.Models;
using Fonte.Core.Training;

namespace Fonte.Core.Tests;

public sealed class TrainingTests
{
    [Fact]
    public void Plates_are_loaded_heaviest_first()
    {
        var load = Plates.Load(102.5, 20);

        Assert.Equal([25, 15, 1.25], load.PerSide);
        Assert.Equal((102.5, 0d), (load.Achieved, load.Missing(102.5)));
    }

    [Fact]
    public void A_load_the_plates_cannot_make_is_rounded_down()
    {
        var load = Plates.Load(101, 20, [20, 10, 5]);

        Assert.Equal([20, 20], load.PerSide);
        Assert.Equal((100d, 1d), (load.Achieved, load.Missing(101)));
        Assert.Empty(Plates.Load(15, 20).PerSide);
    }

    [Fact]
    public void The_warm_up_climbs_to_the_working_weight()
    {
        Assert.Equal(
            [new WarmUpSet(20, 10), new WarmUpSet(40, 8), new WarmUpSet(60, 5), new WarmUpSet(80, 3)],
            Plates.WarmUp(102.5, 20));
        Assert.Equal([new WarmUpSet(20, 10), new WarmUpSet(22.5, 3)], Plates.WarmUp(30, 20));
        Assert.Empty(Plates.WarmUp(20, 20));
    }

    [Theory]
    [InlineData(Equipment.Barbell, 2.5)]
    [InlineData(Equipment.Dumbbell, 2)]
    [InlineData(Equipment.Kettlebell, 4)]
    [InlineData(Equipment.Bodyweight, 0)]
    public void The_load_goes_up_by_the_equipment_s_step(Equipment equipment, double step)
    {
        Assert.Equal(step, Progression.Increment(equipment));
    }

    [Fact]
    public void Bodyweight_and_timed_exercises_progress_in_reps_and_seconds()
    {
        var pullUp = new Exercise { Equipment = Equipment.Bodyweight, Tracking = Tracking.Reps };
        var reps = new TemplateExercise { Sets = 2, Reps = 8 };
        Assert.Equal(ProgressionKind.Reps, Progression.Apply(pullUp, reps, [new() { Reps = 8 }, new() { Reps = 9 }])!.Kind);
        Assert.Equal(9, reps.Reps);

        var plank = new Exercise { Equipment = Equipment.Bodyweight, Tracking = Tracking.Time };
        var hold = new TemplateExercise { Sets = 2, Seconds = 45 };
        Assert.Null(Progression.Apply(plank, hold, [new() { Seconds = 45 }, new() { Seconds = 40 }]));
        Assert.NotNull(Progression.Apply(plank, hold, [new() { Seconds = 45 }, new() { Seconds = 50 }]));
        Assert.Equal(50, hold.Seconds);
    }

    [Fact]
    public void Fewer_sets_than_planned_never_progress()
    {
        var squat = new Exercise { Equipment = Equipment.Barbell };
        var target = new TemplateExercise { Sets = 3, Reps = 5, Weight = 100 };

        Assert.Null(Progression.Apply(squat, target, [new() { Weight = 100, Reps = 5 }, new() { Weight = 100, Reps = 5 }]));
        Assert.Equal(100, target.Weight);
    }

    [Fact]
    public void Regular_weeks_count_back_from_now()
    {
        var today = new DateTime(2026, 9, 30); // a Wednesday
        DateTime[] dates =
        [
            new(2026, 9, 7), new(2026, 9, 9), // 2 that week
            new(2026, 9, 14), new(2026, 9, 18), new(2026, 9, 19),
            new(2026, 9, 22), new(2026, 9, 25),
            new(2026, 9, 29), // this week, goal not met yet
        ];

        Assert.Equal(3, Streaks.RegularWeeks(dates, 2, today, DayOfWeek.Monday));
        Assert.Equal(4, Streaks.RegularWeeks([.. dates, today], 2, today, DayOfWeek.Monday));
        Assert.Equal(0, Streaks.RegularWeeks(dates, 3, today, DayOfWeek.Monday)); // the week of the 21st breaks it
        Assert.Equal(new DateTime(2026, 9, 27), Streaks.WeekStart(today, DayOfWeek.Sunday));
    }
}
