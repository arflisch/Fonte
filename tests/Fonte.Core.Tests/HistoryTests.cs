using Fonte.Core.Models;

namespace Fonte.Core.Tests;

public sealed class HistoryTests : StoreTestBase
{
    [Fact]
    public async Task A_month_lists_its_workouts_with_their_totals()
    {
        var bench = await IdOf("bench_press");
        await DoWorkoutAsync(Monday.AddDays(-7), (bench, [(80, 5), (80, 5)]));
        await DoWorkoutAsync(Monday, (bench, [(82.5, 5)]));
        await DoWorkoutAsync(Monday.AddDays(5), (bench, [(80, 5)]));

        var september = await Store.GetMonthAsync(2026, 9);

        Assert.Equal(2, september.Workouts.Count);
        Assert.Equal(Monday, september.Workouts[0].Workout.StartedAt);
        Assert.Equal((2, 3, 1212.5), (september.Days.Count, september.SetCount, september.Volume));
        Assert.Equal(TimeSpan.FromMinutes(90), september.Duration);
        Assert.Equal(3, (await Store.GetWorkoutDatesAsync()).Count);
    }

    [Fact]
    public async Task Progress_counts_weeks_muscles_and_this_month_s_records()
    {
        var bench = await IdOf("bench_press");
        await DoWorkoutAsync(Monday.AddDays(-7), (bench, [(80, 5), (80, 5)]));
        await DoWorkoutAsync(Monday.AddDays(-2), (bench, [(85, 5)]), (await IdOf("plank"), []));

        var report = await Store.GetProgressAsync(Monday, DayOfWeek.Monday);

        Assert.Equal(12, report.Weeks.Count);
        Assert.Equal(Monday.Date, report.Weeks[^1].Start);
        Assert.Equal((0, 2, 3), (report.Weeks[^1].Workouts, report.Weeks[^2].Workouts, report.Weeks[^2].Sets));
        Assert.Equal(1, report.MuscleSets[MuscleGroup.Chest]); // the last seven days only
        Assert.Equal([MuscleGroup.Back, MuscleGroup.Shoulders, MuscleGroup.Arms, MuscleGroup.Legs, MuscleGroup.Core], report.Neglected);
        var record = Assert.Single(report.MonthRecords);
        Assert.Equal((85d, Monday.AddDays(-2)), (record.Record.Set.Weight, record.Date));
    }

    [Fact]
    public async Task Muscles_are_not_called_neglected_without_regular_training()
    {
        await DoWorkoutAsync(Monday.AddDays(-1), (await IdOf("bench_press"), [(80, 5)]));

        Assert.Empty((await Store.GetProgressAsync(Monday, DayOfWeek.Monday)).Neglected);
    }

    [Fact]
    public async Task One_weight_and_one_measurement_of_each_kind_per_day()
    {
        await Store.SaveWeightAsync(Monday, 80);
        await Store.SaveWeightAsync(Monday.AddHours(2), 79.64);
        await Store.SaveWeightAsync(Monday.AddDays(-7), 81);
        await Store.SaveMeasurementAsync(Monday, MeasurementKind.Waist, 84);
        await Store.SaveMeasurementAsync(Monday, MeasurementKind.Waist, 83);
        await Store.SaveMeasurementAsync(Monday, MeasurementKind.Arms, 36.5);

        Assert.Equal([81d, 79.6], (await Store.GetWeightsAsync()).Select(w => w.Kilograms));
        Assert.Equal([(MeasurementKind.Waist, 83d), (MeasurementKind.Arms, 36.5)],
            (await Store.GetMeasurementsAsync()).Select(m => (m.Kind, m.Centimetres)));
        var error = await Assert.ThrowsAsync<FonteException>(() => Store.SaveWeightAsync(Monday, 7));
        Assert.Equal(FonteError.InvalidBodyValue, error.Error);
    }

    [Fact]
    public async Task Deleting_a_photo_gives_back_its_file()
    {
        var photo = await Store.AddPhotoAsync(Monday, "photo-1.jpg");
        await Store.AddPhotoAsync(Monday.AddDays(7), "photo-2.jpg");

        Assert.Equal(["photo-2.jpg", "photo-1.jpg"], (await Store.GetPhotosAsync()).Select(p => p.FileName));
        Assert.Equal("photo-1.jpg", await Store.DeletePhotoAsync(photo.Id));
        Assert.Null(await Store.DeletePhotoAsync(photo.Id));
    }
}
