using Fonte.Core.Models;
using Fonte.Core.Training;
using SQLite;

namespace Fonte.Core.Data;

public sealed partial class FonteStore
{
    public async Task<WorkoutSummary?> GetWorkoutSummaryAsync(int workoutId)
    {
        var db = await GetConnectionAsync();
        var workout = await db.FindAsync<Workout>(workoutId);
        return workout is null ? null : await BuildSummaryAsync(db, workout, await GetDoneSetsAsync(db));
    }

    /// <summary>The last finished workouts, most recent first.</summary>
    public async Task<IReadOnlyList<WorkoutSummary>> GetRecentWorkoutsAsync(int count)
    {
        var db = await GetConnectionAsync();
        var workouts = await db.Table<Workout>()
            .Where(w => w.FinishedAt != null)
            .OrderByDescending(w => w.StartedAt)
            .Take(count)
            .ToListAsync();
        var done = await GetDoneSetsAsync(db);

        var summaries = new List<WorkoutSummary>();
        foreach (var workout in workouts)
            summaries.Add(await BuildSummaryAsync(db, workout, done));
        return summaries;
    }

    /// <summary>Days with at least one finished workout between <paramref name="from"/> and <paramref name="to"/> (included).</summary>
    public async Task<IReadOnlySet<DateTime>> GetWorkoutDaysAsync(DateTime from, DateTime to)
    {
        var db = await GetConnectionAsync();
        var start = from.Date;
        var end = to.Date.AddDays(1);
        var workouts = await db.Table<Workout>()
            .Where(w => w.FinishedAt != null && w.StartedAt >= start && w.StartedAt < end)
            .ToListAsync();
        return workouts.Select(w => w.StartedAt.Date).ToHashSet();
    }

    /// <summary>Every finished workout where the exercise was done, most recent first, and its best set.</summary>
    public async Task<ExerciseHistory> GetExerciseHistoryAsync(int exerciseId)
    {
        var db = await GetConnectionAsync();
        var exercise = await db.FindAsync<Exercise>(exerciseId)
            ?? throw new FonteException(FonteError.ExerciseNotFound, $"Exercise {exerciseId} does not exist.");
        var rows = (await GetDoneSetsAsync(db)).Where(r => r.ExerciseId == exerciseId).ToList();
        var workouts = (await db.Table<Workout>().Where(w => w.FinishedAt != null).ToListAsync()).ToDictionary(w => w.Id);

        var sessions = rows
            .GroupBy(r => r.WorkoutId)
            .Where(g => workouts.ContainsKey(g.Key))
            .Select(g => new ExerciseSession(workouts[g.Key], g.OrderBy(r => r.WorkoutExerciseId).ThenBy(r => r.Position).Select(r => r.ToSet()).ToList()))
            .OrderByDescending(s => s.Workout.StartedAt)
            .ToList();
        return new ExerciseHistory(exercise, sessions, Performance.Best(exercise.Tracking, sessions.SelectMany(s => s.Sets)));
    }

    /// <summary>
    /// What was done in a workout, and its personal records: sets that beat the best done on that exercise in
    /// earlier workouts. The first time an exercise is done sets no record, there is nothing to beat yet.
    /// </summary>
    private static async Task<WorkoutSummary> BuildSummaryAsync(SQLiteAsyncConnection db, Workout workout, List<DoneSetRow> done)
    {
        var items = await db.Table<WorkoutExercise>().Where(i => i.WorkoutId == workout.Id).OrderBy(i => i.Position).ToListAsync();
        var recaps = new List<ExerciseRecap>();
        var records = new List<PersonalRecord>();

        foreach (var item in items)
        {
            var exercise = await db.FindAsync<Exercise>(item.ExerciseId);
            if (exercise is null)
                continue;
            var sets = await db.Table<WorkoutSet>()
                .Where(s => s.WorkoutExerciseId == item.Id && s.IsDone)
                .OrderBy(s => s.Position)
                .ToListAsync();
            if (sets.Count == 0)
                continue;
            recaps.Add(new ExerciseRecap(exercise, sets));

            var before = done
                .Where(r => r.ExerciseId == exercise.Id && r.StartedAt < workout.StartedAt)
                .Select(r => Performance.Score(exercise.Tracking, r.ToSet()))
                .DefaultIfEmpty(0)
                .Max();
            var best = Performance.Best(exercise.Tracking, sets)!;
            if (before > 0 && Performance.Score(exercise.Tracking, best) > before)
                records.Add(new PersonalRecord(exercise, best, before));
        }
        return new WorkoutSummary(workout, recaps, records);
    }
}
