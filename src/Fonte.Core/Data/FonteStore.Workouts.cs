using Fonte.Core.Models;
using Fonte.Core.Training;
using SQLite;

namespace Fonte.Core.Data;

public sealed partial class FonteStore
{
    /// <summary>Sets proposed for an exercise done for the first time.</summary>
    private const int DefaultSetCount = 3;

    /// <summary>The workout started and not finished yet, if any (there is at most one).</summary>
    public async Task<Workout?> GetWorkoutInProgressAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<Workout>().Where(w => w.FinishedAt == null).FirstOrDefaultAsync();
    }

    /// <summary>Starts a workout, or returns the one already in progress.</summary>
    public async Task<Workout> StartWorkoutAsync(DateTime now)
    {
        if (await GetWorkoutInProgressAsync() is { } inProgress)
            return inProgress;

        var db = await GetConnectionAsync();
        var workout = new Workout { StartedAt = now };
        await db.InsertAsync(workout);
        OnChanged();
        return workout;
    }

    /// <summary>A workout with its exercises, their sets, and what was done the previous time on each exercise.</summary>
    public async Task<WorkoutDetail> GetWorkoutDetailAsync(int workoutId)
    {
        var db = await GetConnectionAsync();
        var workout = await FindWorkoutAsync(db, workoutId);
        var items = await db.Table<WorkoutExercise>().Where(i => i.WorkoutId == workoutId).OrderBy(i => i.Position).ToListAsync();
        var done = await GetDoneSetsAsync(db);

        var entries = new List<WorkoutEntry>();
        foreach (var item in items)
        {
            var exercise = await db.FindAsync<Exercise>(item.ExerciseId);
            if (exercise is null)
                continue;
            var sets = await db.Table<WorkoutSet>().Where(s => s.WorkoutExerciseId == item.Id).OrderBy(s => s.Position).ToListAsync();
            entries.Add(new WorkoutEntry(item, exercise, sets, PreviousSets(done, exercise.Id, workout)));
        }
        return new WorkoutDetail(workout, entries);
    }

    /// <summary>
    /// Adds an exercise at the end of the workout, with the sets done last time already filled in (loads and
    /// repetitions to beat), or three empty sets the first time.
    /// </summary>
    public async Task<WorkoutExercise> AddExerciseToWorkoutAsync(int workoutId, int exerciseId)
    {
        var db = await GetConnectionAsync();
        var workout = await FindWorkoutAsync(db, workoutId);
        if (await db.FindAsync<Exercise>(exerciseId) is null)
            throw new FonteException(FonteError.ExerciseNotFound, $"Exercise {exerciseId} does not exist.");

        var previous = PreviousSets(await GetDoneSetsAsync(db), exerciseId, workout);
        var position = await db.ExecuteScalarAsync<int>(
            "SELECT IFNULL(MAX(Position), -1) + 1 FROM workout_exercises WHERE WorkoutId = ?", workoutId);
        var item = new WorkoutExercise { WorkoutId = workoutId, ExerciseId = exerciseId, Position = position };

        await db.RunInTransactionAsync(conn =>
        {
            conn.Insert(item);
            var template = previous.Count > 0
                ? previous
                : Enumerable.Range(0, DefaultSetCount).Select(_ => new WorkoutSet()).ToList();
            for (var i = 0; i < template.Count; i++)
            {
                conn.Insert(new WorkoutSet
                {
                    WorkoutExerciseId = item.Id,
                    Position = i,
                    Weight = template[i].Weight,
                    Reps = template[i].Reps,
                    Seconds = template[i].Seconds,
                });
            }
        });
        OnChanged();
        return item;
    }

    /// <summary>Adds a set after the last one, with the same load and repetitions.</summary>
    public async Task<WorkoutSet> AddSetAsync(int workoutExerciseId)
    {
        var db = await GetConnectionAsync();
        var last = await db.Table<WorkoutSet>()
            .Where(s => s.WorkoutExerciseId == workoutExerciseId)
            .OrderByDescending(s => s.Position)
            .FirstOrDefaultAsync();
        var set = new WorkoutSet
        {
            WorkoutExerciseId = workoutExerciseId,
            Position = (last?.Position ?? -1) + 1,
            Weight = last?.Weight ?? 0,
            Reps = last?.Reps ?? 0,
            Seconds = last?.Seconds ?? 0,
        };
        await db.InsertAsync(set);
        return set;
    }

    public async Task UpdateSetAsync(int setId, double weight, int reps, int seconds)
    {
        var db = await GetConnectionAsync();
        var set = await FindSetAsync(db, setId);
        set.Weight = Math.Max(0, Math.Round(weight, 2));
        set.Reps = Math.Max(0, reps);
        set.Seconds = Math.Max(0, seconds);
        await db.UpdateAsync(set);
    }

    /// <summary>Ticks or unticks a set. A set can only be ticked once it says what was done.</summary>
    /// <exception cref="FonteException">Ticking a set without repetitions (or duration).</exception>
    public async Task<WorkoutSet> SetDoneAsync(int setId, bool done, DateTime now)
    {
        var db = await GetConnectionAsync();
        var set = await FindSetAsync(db, setId);
        if (done)
        {
            var tracking = await db.ExecuteScalarAsync<int>(
                "SELECT e.Tracking FROM exercises e JOIN workout_exercises we ON we.ExerciseId = e.Id WHERE we.Id = ?",
                set.WorkoutExerciseId);
            var complete = (Tracking)tracking == Tracking.Time ? set.Seconds > 0 : set.Reps > 0;
            if (!complete)
                throw new FonteException(FonteError.IncompleteSet, $"Set {setId} has nothing to tick.");
        }

        set.IsDone = done;
        set.DoneAt = done ? now : null;
        await db.UpdateAsync(set);
        return set;
    }

    public async Task DeleteSetAsync(int setId)
    {
        var db = await GetConnectionAsync();
        var set = await FindSetAsync(db, setId);
        await db.RunInTransactionAsync(conn =>
        {
            conn.Delete(set);
            conn.Execute(
                "UPDATE workout_sets SET Position = Position - 1 WHERE WorkoutExerciseId = ? AND Position > ?",
                set.WorkoutExerciseId, set.Position);
        });
    }

    public async Task RemoveExerciseFromWorkoutAsync(int workoutExerciseId)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM workout_sets WHERE WorkoutExerciseId = ?", workoutExerciseId);
            conn.Delete<WorkoutExercise>(workoutExerciseId);
        });
        OnChanged();
    }

    /// <summary>Moves an exercise of the workout one place up (<paramref name="delta"/> = -1) or down (+1).</summary>
    public async Task MoveWorkoutExerciseAsync(int workoutExerciseId, int delta)
    {
        var db = await GetConnectionAsync();
        var item = await db.FindAsync<WorkoutExercise>(workoutExerciseId)
            ?? throw new FonteException(FonteError.WorkoutNotFound, $"Workout exercise {workoutExerciseId} does not exist.");
        var siblings = await db.Table<WorkoutExercise>().Where(i => i.WorkoutId == item.WorkoutId).OrderBy(i => i.Position).ToListAsync();
        if (Swap(siblings, item.Id, delta) is { } changed)
        {
            await db.UpdateAllAsync(changed);
            OnChanged();
        }
    }

    /// <summary>Links an exercise to the next one (superset): no rest between them.</summary>
    public async Task SetWorkoutExerciseLinkAsync(int workoutExerciseId, bool linkedToNext)
    {
        var db = await GetConnectionAsync();
        var item = await db.FindAsync<WorkoutExercise>(workoutExerciseId)
            ?? throw new FonteException(FonteError.WorkoutNotFound, $"Workout exercise {workoutExerciseId} does not exist.");
        item.LinkedToNext = linkedToNext;
        await db.UpdateAsync(item);
        OnChanged();
    }

    /// <summary>
    /// Ends the workout: sets left unticked are dropped, and so are exercises without any ticked set. A workout
    /// where nothing was ticked is deleted and null is returned. A workout started from a template raises the
    /// template's targets where every planned set succeeded (<see cref="WorkoutSummary.Progressions"/>).
    /// </summary>
    public async Task<WorkoutSummary?> FinishWorkoutAsync(int workoutId, DateTime now)
    {
        var db = await GetConnectionAsync();
        var workout = await FindWorkoutAsync(db, workoutId);

        var kept = 0;
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute(
                "DELETE FROM workout_sets WHERE IsDone = 0 AND WorkoutExerciseId IN (SELECT Id FROM workout_exercises WHERE WorkoutId = ?)",
                workoutId);
            conn.Execute(
                "DELETE FROM workout_exercises WHERE WorkoutId = ? AND Id NOT IN (SELECT WorkoutExerciseId FROM workout_sets)",
                workoutId);
            kept = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM workout_exercises WHERE WorkoutId = ?", workoutId);
            if (kept == 0)
            {
                conn.Delete(workout);
                return;
            }
            workout.FinishedAt = now;
            conn.Update(workout);
        });
        if (kept == 0)
        {
            OnChanged();
            return null;
        }

        var progressions = workout.TemplateId is { } templateId ? await ApplyProgressionAsync(db, workoutId, templateId) : [];
        OnChanged();
        return await GetWorkoutSummaryAsync(workoutId) is { } summary ? summary with { Progressions = progressions } : null;
    }

    private static async Task<IReadOnlyList<ProgressionStep>> ApplyProgressionAsync(SQLiteAsyncConnection db, int workoutId, int templateId)
    {
        if (await db.FindAsync<WorkoutTemplate>(templateId) is null)
            return [];

        var targets = await db.Table<TemplateExercise>().Where(t => t.TemplateId == templateId).ToListAsync();
        var items = await db.Table<WorkoutExercise>().Where(i => i.WorkoutId == workoutId).ToListAsync();
        var steps = new List<ProgressionStep>();
        foreach (var target in targets)
        {
            var exercise = await db.FindAsync<Exercise>(target.ExerciseId);
            if (exercise is null)
                continue;
            var sets = new List<WorkoutSet>();
            foreach (var item in items.Where(i => i.ExerciseId == target.ExerciseId))
                sets.AddRange(await db.Table<WorkoutSet>().Where(s => s.WorkoutExerciseId == item.Id && s.IsDone).ToListAsync());

            var before = (target.Weight, target.Reps, target.Seconds);
            if (Progression.Apply(exercise, target, sets) is { } step)
                steps.Add(step);
            if ((target.Weight, target.Reps, target.Seconds) != before)
                await db.UpdateAsync(target);
        }
        return steps;
    }

    /// <summary>Deletes a workout (in progress or finished) and everything done in it.</summary>
    public async Task DeleteWorkoutAsync(int workoutId)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute(
                "DELETE FROM workout_sets WHERE WorkoutExerciseId IN (SELECT Id FROM workout_exercises WHERE WorkoutId = ?)",
                workoutId);
            conn.Execute("DELETE FROM workout_exercises WHERE WorkoutId = ?", workoutId);
            conn.Delete<Workout>(workoutId);
        });
        OnChanged();
    }

    /// <summary>How the workout felt (1 to 5) and a free note.</summary>
    public async Task SaveWorkoutReviewAsync(int workoutId, int? feeling, string? note)
    {
        var db = await GetConnectionAsync();
        var workout = await FindWorkoutAsync(db, workoutId);
        workout.Feeling = feeling is >= 1 and <= 5 ? feeling : null;
        workout.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.UpdateAsync(workout);
        OnChanged();
    }

    /// <summary>The sets of the last finished workout, before <paramref name="workout"/>, where the exercise was done.</summary>
    private static IReadOnlyList<WorkoutSet> PreviousSets(List<DoneSetRow> done, int exerciseId, Workout workout)
    {
        var last = done
            .Where(r => r.ExerciseId == exerciseId && r.WorkoutId != workout.Id && r.StartedAt < workout.StartedAt)
            .MaxBy(r => r.StartedAt);
        return last is null
            ? []
            : done.Where(r => r.WorkoutId == last.WorkoutId && r.ExerciseId == exerciseId)
                .OrderBy(r => r.WorkoutExerciseId).ThenBy(r => r.Position)
                .Select(r => r.ToSet())
                .ToList();
    }

    private static async Task<Workout> FindWorkoutAsync(SQLiteAsyncConnection db, int workoutId) =>
        await db.FindAsync<Workout>(workoutId)
            ?? throw new FonteException(FonteError.WorkoutNotFound, $"Workout {workoutId} does not exist.");

    private static async Task<WorkoutSet> FindSetAsync(SQLiteAsyncConnection db, int setId) =>
        await db.FindAsync<WorkoutSet>(setId)
            ?? throw new FonteException(FonteError.SetNotFound, $"Set {setId} does not exist.");
}
