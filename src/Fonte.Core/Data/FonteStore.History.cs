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
        var template = workout.TemplateId is { } templateId ? await db.FindAsync<WorkoutTemplate>(templateId) : null;
        return new WorkoutSummary(workout, recaps, records)
        {
            TemplateName = template?.Name,
            Previous = await FindPreviousAsync(db, workout, done),
        };
    }

    /// <summary>The workout done before this one from the same template (or before it at all, for a free workout).</summary>
    private static async Task<WorkoutComparison?> FindPreviousAsync(SQLiteAsyncConnection db, Workout workout, List<DoneSetRow> done)
    {
        var started = workout.StartedAt;
        var candidates = await db.Table<Workout>()
            .Where(w => w.FinishedAt != null && w.StartedAt < started && w.Id != workout.Id)
            .ToListAsync();
        var previous = candidates
            .Where(w => workout.TemplateId is null || w.TemplateId == workout.TemplateId)
            .MaxBy(w => w.StartedAt);
        if (previous is null)
            return null;
        var sets = done.Where(r => r.WorkoutId == previous.Id).Select(r => r.ToSet()).ToList();
        return new WorkoutComparison(previous, Performance.Volume(sets), sets.Count);
    }

    /// <summary>The finished workouts of a calendar month, most recent first.</summary>
    public async Task<MonthReport> GetMonthAsync(int year, int month)
    {
        var db = await GetConnectionAsync();
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);
        var workouts = await db.Table<Workout>()
            .Where(w => w.FinishedAt != null && w.StartedAt >= start && w.StartedAt < end)
            .OrderByDescending(w => w.StartedAt)
            .ToListAsync();
        var done = await GetDoneSetsAsync(db);
        var summaries = new List<WorkoutSummary>();
        foreach (var workout in workouts)
            summaries.Add(await BuildSummaryAsync(db, workout, done));
        return new MonthReport(year, month, summaries);
    }

    /// <summary>When every finished workout started.</summary>
    public async Task<IReadOnlyList<DateTime>> GetWorkoutDatesAsync()
    {
        var db = await GetConnectionAsync();
        return (await db.Table<Workout>().Where(w => w.FinishedAt != null).ToListAsync()).Select(w => w.StartedAt).ToList();
    }

    /// <summary>
    /// Training over the last <paramref name="weeks"/> weeks: workouts, sets and volume per week, sets per muscle
    /// group over the last seven days, muscle groups left aside, and this month's records.
    /// </summary>
    public async Task<ProgressReport> GetProgressAsync(DateTime today, DayOfWeek firstDay, int weeks = 12)
    {
        var db = await GetConnectionAsync();
        var done = await GetDoneSetsAsync(db);
        var muscles = (await db.Table<Exercise>().ToListAsync()).ToDictionary(e => e.Id, e => e.Muscle);

        var thisWeek = Streaks.WeekStart(today, firstDay);
        var stats = Enumerable.Range(0, weeks)
            .Select(i => thisWeek.AddDays(-7 * (weeks - 1 - i)))
            .Select(start =>
            {
                var rows = done.Where(r => r.StartedAt >= start && r.StartedAt < start.AddDays(7)).ToList();
                return new WeekStat(start, rows.Select(r => r.WorkoutId).Distinct().Count(), rows.Count, Performance.Volume(rows.Select(r => r.ToSet())));
            })
            .ToList();

        var recent = done.Where(r => r.StartedAt >= today.Date.AddDays(-6)).ToList();
        var muscleSets = recent
            .GroupBy(r => muscles.GetValueOrDefault(r.ExerciseId))
            .ToDictionary(g => g.Key, g => g.Count());

        // Only meaningful while training goes on: at least two workouts in the last two weeks.
        var lastTwoWeeks = done.Where(r => r.StartedAt >= today.Date.AddDays(-13)).ToList();
        var neglected = lastTwoWeeks.Select(r => r.WorkoutId).Distinct().Count() < 2
            ? []
            : Enum.GetValues<MuscleGroup>()
                .Where(m => m != MuscleGroup.Cardio && !lastTwoWeeks.Any(r => muscles.GetValueOrDefault(r.ExerciseId) == m))
                .ToList();

        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthWorkouts = await db.Table<Workout>()
            .Where(w => w.FinishedAt != null && w.StartedAt >= monthStart)
            .OrderByDescending(w => w.StartedAt)
            .ToListAsync();
        var records = new List<DatedRecord>();
        foreach (var workout in monthWorkouts)
            records.AddRange((await BuildSummaryAsync(db, workout, done)).Records.Select(r => new DatedRecord(r, workout.StartedAt)));

        return new ProgressReport(stats, muscleSets, neglected, records);
    }
}
