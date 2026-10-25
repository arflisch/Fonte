using Fonte.Core.Models;
using Fonte.Core.Training;
using SQLite;

namespace Fonte.Core.Data;

/// <summary>What an import would bring.</summary>
/// <param name="NewWorkouts">Workouts not already in Fonte (an import done twice adds nothing the second time).</param>
/// <param name="NewExercises">Names of the exercises that will be created; the others become built-in or own exercises.</param>
public sealed record ImportPreview(int Workouts, int NewWorkouts, int Sets, DateTime? From, DateTime? To, IReadOnlyList<string> NewExercises);

public sealed record ImportResult(int Workouts, int Sets, int NewExercises);

public sealed partial class FonteStore
{
    public async Task<ImportPreview> PreviewImportAsync(IReadOnlyList<ImportedWorkout> workouts)
    {
        var db = await GetConnectionAsync();
        var known = await KnownStartsAsync(db);
        var fresh = workouts.Where(w => !known.Contains(Minute(w.StartedAt))).ToList();
        var exercises = await db.Table<Exercise>().ToListAsync();
        var created = WorkoutImport.Match(fresh)
            .Where(m => Resolve(m, exercises) is null)
            .Select(m => m.Name)
            .ToList();
        return new ImportPreview(
            workouts.Count, fresh.Count, fresh.Sum(w => w.Exercises.Sum(e => e.Sets.Count)),
            workouts.Count > 0 ? workouts.Min(w => w.StartedAt) : null,
            workouts.Count > 0 ? workouts.Max(w => w.StartedAt) : null,
            created);
    }

    /// <summary>
    /// Adds the workouts of another app as finished workouts, with every set ticked, skipping those that start at
    /// the same minute as a workout already in Fonte. Exercises are matched by name; the unknown ones are created.
    /// </summary>
    public async Task<ImportResult> ImportAsync(IReadOnlyList<ImportedWorkout> workouts)
    {
        var db = await GetConnectionAsync();
        var known = await KnownStartsAsync(db);
        var fresh = workouts.Where(w => !known.Contains(Minute(w.StartedAt))).OrderBy(w => w.StartedAt).ToList();
        var exercises = await db.Table<Exercise>().ToListAsync();
        var matches = WorkoutImport.Match(fresh);

        int sets = 0, created = 0;
        await db.RunInTransactionAsync(conn =>
        {
            var ids = new Dictionary<string, int>();
            foreach (var match in matches)
            {
                if (Resolve(match, exercises) is { } existing)
                {
                    ids[match.Name] = existing.Id;
                    continue;
                }
                var exercise = new Exercise
                {
                    CustomName = match.Name.Length > 60 ? match.Name[..60].Trim() : match.Name.Trim(),
                    Muscle = match.Muscle,
                    Equipment = match.Equipment,
                    Tracking = match.Tracking,
                };
                conn.Insert(exercise);
                exercises.Add(exercise);
                ids[match.Name] = exercise.Id;
                created++;
            }

            foreach (var imported in fresh)
            {
                var note = string.Join(" · ", new[] { imported.Title, imported.Note }.Where(t => !string.IsNullOrWhiteSpace(t)));
                var workout = new Workout
                {
                    StartedAt = imported.StartedAt,
                    FinishedAt = imported.FinishedAt,
                    Note = note.Length == 0 ? null : note.Length > 500 ? note[..500] : note,
                };
                conn.Insert(workout);
                for (var i = 0; i < imported.Exercises.Count; i++)
                {
                    var item = imported.Exercises[i];
                    var next = i + 1 < imported.Exercises.Count ? imported.Exercises[i + 1] : null;
                    var row = new WorkoutExercise
                    {
                        WorkoutId = workout.Id,
                        ExerciseId = ids[item.Name],
                        Position = i,
                        LinkedToNext = item.SupersetId is { } superset && next?.SupersetId == superset,
                    };
                    conn.Insert(row);
                    var at = imported.StartedAt;
                    conn.InsertAll(item.Sets.Select((s, position) => new WorkoutSet
                    {
                        WorkoutExerciseId = row.Id,
                        Position = position,
                        Weight = s.Weight,
                        Reps = s.Reps,
                        Seconds = s.Seconds,
                        Kind = s.Kind,
                        Rpe = s.Rpe,
                        IsDone = true,
                        DoneAt = at,
                    }));
                    sets += item.Sets.Count;
                }
            }
        });
        if (fresh.Count > 0)
            OnChanged();
        return new ImportResult(fresh.Count, sets, created);
    }

    /// <summary>The Fonte exercise an imported one becomes, when it already exists.</summary>
    private static Exercise? Resolve(ImportedExerciseMatch match, IReadOnlyList<Exercise> exercises) =>
        match.CatalogKey is { } key
            ? exercises.FirstOrDefault(e => e.CatalogKey == key)
            : exercises
                .Where(e => e.IsCustom && e.CustomName is not null && WorkoutImport.Normalize(e.CustomName) == WorkoutImport.Normalize(match.Name))
                .OrderBy(e => e.IsArchived)
                .FirstOrDefault();

    private static async Task<HashSet<DateTime>> KnownStartsAsync(SQLiteAsyncConnection db) =>
        (await db.QueryScalarsAsync<DateTime>("SELECT StartedAt FROM workouts")).Select(Minute).ToHashSet();

    private static DateTime Minute(DateTime date) => new(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0);
}
