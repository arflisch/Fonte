using Fonte.Core.Catalog;
using Fonte.Core.Data;
using Fonte.Core.Models;

namespace Fonte.Core.Tests;

public sealed class FonteStoreTests : IAsyncLifetime
{
    private static readonly DateTime Monday = new(2026, 9, 28, 18, 0, 0);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fonte-{Guid.NewGuid():N}.db3");
    private FonteStore _store = null!;

    public Task InitializeAsync()
    {
        _store = new FonteStore(_path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
            File.Delete(file);
    }

    [Fact]
    public async Task The_library_starts_with_the_catalog()
    {
        var exercises = await _store.GetExercisesAsync();

        Assert.Equal(ExerciseCatalog.All.Count, exercises.Count);
        Assert.All(exercises, e => Assert.False(e.IsCustom));
    }

    [Fact]
    public async Task Only_one_workout_is_in_progress_at_a_time()
    {
        var first = await _store.StartWorkoutAsync(Monday);
        var again = await _store.StartWorkoutAsync(Monday.AddMinutes(5));

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.Id, (await _store.GetWorkoutInProgressAsync())!.Id);
    }

    [Fact]
    public async Task A_new_exercise_starts_with_three_empty_sets()
    {
        var workout = await _store.StartWorkoutAsync(Monday);
        await _store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("bench_press"));

        var entry = Assert.Single((await _store.GetWorkoutDetailAsync(workout.Id)).Entries);
        Assert.Equal(3, entry.Sets.Count);
        Assert.All(entry.Sets, s => Assert.Equal((0d, 0, false), (s.Weight, s.Reps, s.IsDone)));
        Assert.Empty(entry.PreviousSets);
    }

    [Fact]
    public async Task An_exercise_done_before_is_prefilled_with_last_time()
    {
        var bench = await IdOf("bench_press");
        await DoWorkoutAsync(Monday, (bench, [(80, 6), (82.5, 5)]));

        var workout = await _store.StartWorkoutAsync(Monday.AddDays(3));
        await _store.AddExerciseToWorkoutAsync(workout.Id, bench);

        var entry = Assert.Single((await _store.GetWorkoutDetailAsync(workout.Id)).Entries);
        Assert.Equal([(80d, 6), (82.5, 5)], entry.Sets.Select(s => (s.Weight, s.Reps)));
        Assert.All(entry.Sets, s => Assert.False(s.IsDone));
        Assert.Equal([(80d, 6), (82.5, 5)], entry.PreviousSets.Select(s => (s.Weight, s.Reps)));
    }

    [Fact]
    public async Task Sets_are_added_like_the_last_one_and_renumbered_when_deleted()
    {
        var workout = await _store.StartWorkoutAsync(Monday);
        var item = await _store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("squat"));
        var sets = (await _store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets;
        await _store.UpdateSetAsync(sets[2].Id, 100, 5, 0);

        var added = await _store.AddSetAsync(item.Id);
        await _store.DeleteSetAsync(sets[0].Id);

        var after = (await _store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets;
        Assert.Equal((100d, 5, 3), (added.Weight, added.Reps, added.Position));
        Assert.Equal([0, 1, 2], after.Select(s => s.Position));
    }

    [Fact]
    public async Task A_set_needs_repetitions_or_a_duration_to_be_ticked()
    {
        var workout = await _store.StartWorkoutAsync(Monday);
        await _store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("plank"));
        var set = (await _store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets[0];

        var error = await Assert.ThrowsAsync<FonteException>(() => _store.SetDoneAsync(set.Id, true, Monday));
        await _store.UpdateSetAsync(set.Id, 0, 0, 45);
        var ticked = await _store.SetDoneAsync(set.Id, true, Monday);

        Assert.Equal(FonteError.IncompleteSet, error.Error);
        Assert.True(ticked.IsDone);
    }

    [Fact]
    public async Task Finishing_keeps_only_ticked_sets()
    {
        var bench = await IdOf("bench_press");
        var workout = await _store.StartWorkoutAsync(Monday);
        await _store.AddExerciseToWorkoutAsync(workout.Id, bench);
        await _store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("lateral_raise"));
        var sets = (await _store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets;
        await _store.UpdateSetAsync(sets[0].Id, 80, 8, 0);
        await _store.SetDoneAsync(sets[0].Id, true, Monday);

        var summary = await _store.FinishWorkoutAsync(workout.Id, Monday.AddMinutes(47));

        Assert.NotNull(summary);
        var recap = Assert.Single(summary.Exercises);
        Assert.Equal(bench, recap.Exercise.Id);
        Assert.Single(recap.Sets);
        Assert.Equal((TimeSpan.FromMinutes(47), 640d, 1), (summary.Duration, summary.Volume, summary.SetCount));
        Assert.Null(await _store.GetWorkoutInProgressAsync());
    }

    [Fact]
    public async Task A_workout_where_nothing_was_ticked_is_dropped()
    {
        var workout = await _store.StartWorkoutAsync(Monday);
        await _store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("squat"));

        Assert.Null(await _store.FinishWorkoutAsync(workout.Id, Monday.AddMinutes(10)));
        Assert.Empty(await _store.GetRecentWorkoutsAsync(10));
    }

    [Fact]
    public async Task Beating_the_best_set_is_a_personal_record_but_the_first_time_is_not()
    {
        var bench = await IdOf("bench_press");
        var first = await DoWorkoutAsync(Monday, (bench, [(80, 5)]));
        var weaker = await DoWorkoutAsync(Monday.AddDays(2), (bench, [(80, 4)]));
        var better = await DoWorkoutAsync(Monday.AddDays(4), (bench, [(80, 5), (82.5, 5)]));

        Assert.Empty(first.Records);
        Assert.Empty(weaker.Records);
        var record = Assert.Single(better.Records);
        Assert.Equal((82.5, 5), (record.Set.Weight, record.Set.Reps));
        Assert.Equal(93.33, Math.Round(record.PreviousBest, 2));
    }

    [Fact]
    public async Task Recent_workouts_and_exercise_history_are_most_recent_first()
    {
        var squat = await IdOf("squat");
        await DoWorkoutAsync(Monday, (squat, [(100, 5)]));
        await DoWorkoutAsync(Monday.AddDays(2), (squat, [(105, 5), (105, 4)]));

        var recent = await _store.GetRecentWorkoutsAsync(10);
        var history = await _store.GetExerciseHistoryAsync(squat);

        Assert.Equal([Monday.AddDays(2), Monday], recent.Select(w => w.Workout.StartedAt));
        Assert.Equal([2, 1], history.Sessions.Select(s => s.Sets.Count));
        Assert.Equal((105d, 5), (history.Best!.Weight, history.Best.Reps));
        Assert.Equal(122.5, Math.Round(history.BestEstimatedOneRepMax, 2));
    }

    [Fact]
    public async Task The_library_shows_each_exercise_best_set()
    {
        var squat = await IdOf("squat");
        await DoWorkoutAsync(Monday, (squat, [(100, 5)]));
        await DoWorkoutAsync(Monday.AddDays(2), (squat, [(90, 8)]));

        var overview = (await _store.GetExerciseOverviewsAsync()).Single(o => o.Exercise.Id == squat);

        // 100 × 5 (≈ 116.7 kg estimated) beats 90 × 8 (≈ 114 kg).
        Assert.Equal((100d, 5, 2), (overview.Best!.Weight, overview.Best.Reps, overview.TimesDone));
        Assert.Equal(Monday.AddDays(2), overview.LastDone);
    }

    [Fact]
    public async Task Workout_days_cover_the_requested_range()
    {
        var squat = await IdOf("squat");
        await DoWorkoutAsync(Monday, (squat, [(100, 5)]));
        await DoWorkoutAsync(Monday.AddDays(2), (squat, [(100, 5)]));
        await DoWorkoutAsync(Monday.AddDays(9), (squat, [(100, 5)]));

        var days = await _store.GetWorkoutDaysAsync(Monday.Date, Monday.Date.AddDays(6));

        Assert.Equal([Monday.Date, Monday.Date.AddDays(2)], days.Order());
    }

    [Fact]
    public async Task Custom_exercises_need_a_name_and_are_hidden_rather_than_deleted_once_used()
    {
        var empty = new Exercise { CustomName = "  ", Muscle = MuscleGroup.Arms, Equipment = Equipment.Cable };
        var error = await Assert.ThrowsAsync<FonteException>(() => _store.SaveCustomExerciseAsync(empty));
        var used = await _store.SaveCustomExerciseAsync(new Exercise { CustomName = " Curl pupitre ", Muscle = MuscleGroup.Arms, Equipment = Equipment.Machine });
        var unused = await _store.SaveCustomExerciseAsync(new Exercise { CustomName = "Tirage menton", Muscle = MuscleGroup.Shoulders, Equipment = Equipment.Barbell });
        await DoWorkoutAsync(Monday, (used.Id, [(25, 10)]));

        await _store.RemoveExerciseAsync(used.Id);
        await _store.RemoveExerciseAsync(unused.Id);

        Assert.Equal(FonteError.EmptyExerciseName, error.Error);
        Assert.Equal("Curl pupitre", used.CustomName);
        Assert.True((await _store.GetExerciseAsync(used.Id))!.IsArchived);
        Assert.Null(await _store.GetExerciseAsync(unused.Id));
        Assert.DoesNotContain(await _store.GetExercisesAsync(), e => e.Id == used.Id);
        Assert.Single((await _store.GetRecentWorkoutsAsync(1))[0].Exercises);
    }

    [Fact]
    public async Task A_review_is_saved_with_the_workout()
    {
        var summary = await DoWorkoutAsync(Monday, (await IdOf("squat"), [(100, 5)]));

        await _store.SaveWorkoutReviewAsync(summary.Workout.Id, 4, "  Genoux OK ");

        var saved = (await _store.GetWorkoutSummaryAsync(summary.Workout.Id))!.Workout;
        Assert.Equal((4, "Genoux OK"), (saved.Feeling, saved.Note));
    }

    private async Task<int> IdOf(string catalogKey) =>
        (await _store.GetExercisesAsync()).Single(e => e.CatalogKey == catalogKey).Id;

    /// <summary>A finished workout where every listed set was ticked.</summary>
    private async Task<Core.Training.WorkoutSummary> DoWorkoutAsync(DateTime start, params (int ExerciseId, (double Weight, int Reps)[] Sets)[] exercises)
    {
        var workout = await _store.StartWorkoutAsync(start);
        foreach (var (exerciseId, sets) in exercises)
        {
            var item = await _store.AddExerciseToWorkoutAsync(workout.Id, exerciseId);
            var existing = (await _store.GetWorkoutDetailAsync(workout.Id)).Entries.Single(e => e.Item.Id == item.Id).Sets;
            for (var i = 0; i < sets.Length; i++)
            {
                var id = i < existing.Count ? existing[i].Id : (await _store.AddSetAsync(item.Id)).Id;
                await _store.UpdateSetAsync(id, sets[i].Weight, sets[i].Reps, 0);
                await _store.SetDoneAsync(id, true, start);
            }
        }
        return (await _store.FinishWorkoutAsync(workout.Id, start.AddMinutes(45)))!;
    }
}
