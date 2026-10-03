using Fonte.Core.Catalog;
using Fonte.Core.Models;
using Fonte.Core.Training;
using SQLite;

namespace Fonte.Core.Data;

/// <summary>Single entry point to the local SQLite database: exercises, workouts and their sets.</summary>
public sealed partial class FonteStore(string databasePath) : IAsyncDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    /// <summary>
    /// Raised after a change the other screens show (exercises, workouts started, finished or deleted). Editing
    /// the sets of the workout in progress does not raise it: only that screen shows them.
    /// </summary>
    public event EventHandler? Changed;

    public async Task<IReadOnlyList<Exercise>> GetExercisesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Exercise>().Where(e => !e.IsArchived).ToListAsync();
    }

    public async Task<Exercise?> GetExerciseAsync(int exerciseId)
    {
        var db = await GetConnectionAsync();
        return await db.FindAsync<Exercise>(exerciseId);
    }

    /// <summary>Every exercise of the library with its best set and when it was last done.</summary>
    public async Task<IReadOnlyList<ExerciseOverview>> GetExerciseOverviewsAsync()
    {
        var db = await GetConnectionAsync();
        var exercises = await db.Table<Exercise>().Where(e => !e.IsArchived).ToListAsync();
        var done = (await GetDoneSetsAsync(db)).ToLookup(r => r.ExerciseId);

        return exercises
            .Select(e =>
            {
                var rows = done[e.Id].ToList();
                return new ExerciseOverview(
                    e,
                    Performance.Best(e.Tracking, rows.Select(r => r.ToSet())),
                    rows.Count > 0 ? rows.Max(r => r.StartedAt) : null,
                    rows.Select(r => r.WorkoutId).Distinct().Count());
            })
            .ToList();
    }

    /// <summary>Creates or renames one of the user's own exercises.</summary>
    public async Task<Exercise> SaveCustomExerciseAsync(Exercise exercise)
    {
        exercise.CustomName = exercise.CustomName?.Trim();
        if (string.IsNullOrEmpty(exercise.CustomName))
            throw new FonteException(FonteError.EmptyExerciseName, "An exercise needs a name.");
        if (!Enum.IsDefined(exercise.Muscle) || !Enum.IsDefined(exercise.Equipment) || !Enum.IsDefined(exercise.Tracking))
            throw new ArgumentOutOfRangeException(nameof(exercise), "Unknown muscle group, equipment or tracking.");
        exercise.CatalogKey = null;

        var db = await GetConnectionAsync();
        if (exercise.Id == 0)
        {
            exercise.CreatedAt = DateTime.Now;
            await db.InsertAsync(exercise);
        }
        else
        {
            await db.UpdateAsync(exercise);
        }
        OnChanged();
        return exercise;
    }

    /// <summary>
    /// Removes an exercise from the library. One that was never done is deleted; otherwise it is only hidden, so
    /// past workouts keep showing it.
    /// </summary>
    public async Task RemoveExerciseAsync(int exerciseId)
    {
        var db = await GetConnectionAsync();
        var exercise = await db.FindAsync<Exercise>(exerciseId)
            ?? throw new FonteException(FonteError.ExerciseNotFound, $"Exercise {exerciseId} does not exist.");

        var used = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM workout_exercises WHERE ExerciseId = ?", exerciseId) > 0;
        if (used || !exercise.IsCustom)
        {
            exercise.IsArchived = true;
            await db.UpdateAsync(exercise);
        }
        else
        {
            await db.DeleteAsync(exercise);
        }
        OnChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection = null;
        }
        _initLock.Dispose();
    }

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
            return _connection;

        await _initLock.WaitAsync();
        try
        {
            if (_connection is null)
            {
                var connection = new SQLiteAsyncConnection(
                    databasePath,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
                await connection.EnableWriteAheadLoggingAsync();
                // Creates the tables, and adds the columns of a newer version to existing ones.
                await connection.CreateTablesAsync<Exercise, Workout, WorkoutExercise, WorkoutSet>();
                await connection.CreateTablesAsync<TrainingProgram, WorkoutTemplate, TemplateExercise>();
                await connection.CreateTablesAsync<BodyWeight, BodyMeasurement, BodyPhoto>();
                await AddMissingCatalogExercisesAsync(connection);
                _connection = connection;
            }
            return _connection;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>First launch, or an update that brings new exercises: adds the catalog entries not in the database.</summary>
    private static async Task AddMissingCatalogExercisesAsync(SQLiteAsyncConnection db)
    {
        var known = (await db.Table<Exercise>().Where(e => e.CatalogKey != null).ToListAsync())
            .Select(e => e.CatalogKey)
            .ToHashSet();
        var missing = ExerciseCatalog.All
            .Where(c => !known.Contains(c.Key))
            .Select(c => new Exercise { CatalogKey = c.Key, Muscle = c.Muscle, Equipment = c.Equipment, Tracking = c.Tracking })
            .ToList();
        if (missing.Count > 0)
            await db.InsertAllAsync(missing);
    }

    /// <summary>Every ticked set of a finished workout, with its exercise and workout.</summary>
    private static Task<List<DoneSetRow>> GetDoneSetsAsync(SQLiteAsyncConnection db) =>
        db.QueryAsync<DoneSetRow>(
            """
            SELECT we.ExerciseId, w.Id AS WorkoutId, w.StartedAt, s.Id AS SetId, s.WorkoutExerciseId,
                   s.Position, s.Weight, s.Reps, s.Seconds
            FROM workout_sets s
            JOIN workout_exercises we ON we.Id = s.WorkoutExerciseId
            JOIN workouts w ON w.Id = we.WorkoutId
            WHERE s.IsDone = 1 AND w.FinishedAt IS NOT NULL
            ORDER BY w.StartedAt, we.Position, s.Position
            """);

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class DoneSetRow
    {
        public int ExerciseId { get; set; }
        public int WorkoutId { get; set; }
        public DateTime StartedAt { get; set; }
        public int SetId { get; set; }
        public int WorkoutExerciseId { get; set; }
        public int Position { get; set; }
        public double Weight { get; set; }
        public int Reps { get; set; }
        public int Seconds { get; set; }

        public WorkoutSet ToSet() => new()
        {
            Id = SetId,
            WorkoutExerciseId = WorkoutExerciseId,
            Position = Position,
            Weight = Weight,
            Reps = Reps,
            Seconds = Seconds,
            IsDone = true,
        };
    }
}
