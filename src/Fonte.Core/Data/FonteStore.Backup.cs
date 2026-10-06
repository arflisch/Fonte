using Fonte.Core.Backup;
using Fonte.Core.Models;
using SQLite;

namespace Fonte.Core.Data;

public sealed partial class FonteStore
{
    /// <summary>Every row of the database, read at one point in time; the photos only when asked.</summary>
    public async Task<BackupData> ExportAsync(bool includePhotos)
    {
        var db = await GetConnectionAsync();
        BackupData? data = null;
        await db.RunInTransactionAsync(conn => data = new BackupData
        {
            Exercises = conn.Table<Exercise>().ToList(),
            Workouts = conn.Table<Workout>().ToList(),
            WorkoutExercises = conn.Table<WorkoutExercise>().ToList(),
            WorkoutSets = conn.Table<WorkoutSet>().ToList(),
            Programs = conn.Table<TrainingProgram>().ToList(),
            Templates = conn.Table<WorkoutTemplate>().ToList(),
            TemplateExercises = conn.Table<TemplateExercise>().ToList(),
            BodyWeights = conn.Table<BodyWeight>().ToList(),
            Measurements = conn.Table<BodyMeasurement>().ToList(),
            Photos = includePhotos ? conn.Table<BodyPhoto>().ToList() : null,
        });
        return data!;
    }

    /// <summary>
    /// Replaces the whole database with a backup's rows, ids included so they still point at each other. A backup
    /// made without photos leaves the current photos in place.
    /// </summary>
    public async Task RestoreAsync(BackupData data)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<WorkoutSet>();
            conn.DeleteAll<WorkoutExercise>();
            conn.DeleteAll<Workout>();
            conn.DeleteAll<TemplateExercise>();
            conn.DeleteAll<WorkoutTemplate>();
            conn.DeleteAll<TrainingProgram>();
            conn.DeleteAll<Exercise>();
            conn.DeleteAll<BodyWeight>();
            conn.DeleteAll<BodyMeasurement>();
            if (data.Photos is not null)
                conn.DeleteAll<BodyPhoto>();

            Insert(conn, data.Exercises);
            Insert(conn, data.Workouts);
            Insert(conn, data.WorkoutExercises);
            Insert(conn, data.WorkoutSets);
            Insert(conn, data.Programs);
            Insert(conn, data.Templates);
            Insert(conn, data.TemplateExercises);
            Insert(conn, data.BodyWeights);
            Insert(conn, data.Measurements);
            if (data.Photos is not null)
                Insert(conn, data.Photos);
        });

        // A backup made by an older version lacks the exercises added to the catalog since.
        await AddMissingCatalogExercisesAsync(db);
        OnChanged();
    }

    /// <summary>"OR REPLACE" writes the ids too, where a plain insert would number the rows anew.</summary>
    private static void Insert<T>(SQLiteConnection conn, IEnumerable<T> rows) =>
        conn.InsertAll(rows, "OR REPLACE", runInTransaction: false);
}
