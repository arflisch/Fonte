using Fonte.Core.Catalog;
using Fonte.Core.Models;
using Fonte.Core.Training;
using SQLite;

namespace Fonte.Core.Data;

public sealed partial class FonteStore
{
    /// <summary>Every program, the one followed first, with its templates and where it stands.</summary>
    public async Task<IReadOnlyList<ProgramOverview>> GetProgramsAsync(DateTime today)
    {
        var db = await GetConnectionAsync();
        var programs = await db.Table<TrainingProgram>().ToListAsync();
        var result = new List<ProgramOverview>();
        foreach (var program in programs.OrderByDescending(p => p.IsActive).ThenBy(p => p.CreatedAt))
            result.Add(await BuildProgramAsync(db, program, today));
        return result;
    }

    /// <summary>The program being followed, if any.</summary>
    public async Task<ProgramOverview?> GetActiveProgramAsync(DateTime today)
    {
        var db = await GetConnectionAsync();
        var program = await db.Table<TrainingProgram>().Where(p => p.IsActive).FirstOrDefaultAsync();
        return program is null ? null : await BuildProgramAsync(db, program, today);
    }

    /// <summary>Templates that belong to no program.</summary>
    public async Task<IReadOnlyList<TemplateOverview>> GetStandaloneTemplatesAsync()
    {
        var db = await GetConnectionAsync();
        var templates = await db.Table<WorkoutTemplate>().Where(t => t.ProgramId == null).OrderBy(t => t.Position).ToListAsync();
        var result = new List<TemplateOverview>();
        foreach (var template in templates)
            result.Add(await BuildTemplateAsync(db, template));
        return result;
    }

    /// <summary>Creates a ready-made program, with its names in the user's language, and follows it from now on.</summary>
    /// <param name="name">Translation of a catalog key ("Program_…", "Template_…").</param>
    public async Task<TrainingProgram> CreateProgramFromCatalogAsync(CatalogProgram catalog, Func<string, string> name, DateTime now)
    {
        var db = await GetConnectionAsync();
        var exercises = (await db.Table<Exercise>().Where(e => e.CatalogKey != null).ToListAsync())
            .ToDictionary(e => e.CatalogKey!);
        var program = new TrainingProgram { Name = name($"Program_{catalog.Key}"), CatalogKey = catalog.Key, Weeks = catalog.Weeks, CreatedAt = now };

        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("UPDATE programs SET IsActive = 0");
            program.IsActive = true;
            program.StartedOn = now.Date;
            conn.Insert(program);
            for (var t = 0; t < catalog.Templates.Count; t++)
            {
                var template = new WorkoutTemplate
                {
                    ProgramId = program.Id,
                    Name = name($"Template_{catalog.Templates[t].Key}"),
                    Position = t,
                    CreatedAt = now,
                };
                conn.Insert(template);
                var items = catalog.Templates[t].Exercises;
                for (var e = 0; e < items.Count; e++)
                {
                    if (!exercises.TryGetValue(items[e].ExerciseKey, out var exercise))
                        continue;
                    conn.Insert(new TemplateExercise
                    {
                        TemplateId = template.Id,
                        ExerciseId = exercise.Id,
                        Position = e,
                        Sets = items[e].Sets,
                        Reps = items[e].Reps,
                        Seconds = items[e].Seconds,
                        LinkedToNext = items[e].LinkedToNext,
                    });
                }
            }
        });
        OnChanged();
        return program;
    }

    /// <summary>An empty program of the user's own, followed from now on.</summary>
    public async Task<TrainingProgram> CreateProgramAsync(string name, DateTime now)
    {
        var db = await GetConnectionAsync();
        var program = new TrainingProgram { Name = CheckName(name), IsActive = true, StartedOn = now.Date, CreatedAt = now };
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("UPDATE programs SET IsActive = 0");
            conn.Insert(program);
        });
        OnChanged();
        return program;
    }

    public async Task RenameProgramAsync(int programId, string name)
    {
        var db = await GetConnectionAsync();
        var program = await FindProgramAsync(db, programId);
        program.Name = CheckName(name);
        await db.UpdateAsync(program);
        OnChanged();
    }

    /// <summary>Follows this program (and no other one); its week 1 starts today.</summary>
    public async Task ActivateProgramAsync(int programId, DateTime now)
    {
        var db = await GetConnectionAsync();
        var program = await FindProgramAsync(db, programId);
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("UPDATE programs SET IsActive = 0");
            program.IsActive = true;
            program.StartedOn = now.Date;
            conn.Update(program);
        });
        OnChanged();
    }

    public async Task StopProgramAsync(int programId)
    {
        var db = await GetConnectionAsync();
        var program = await FindProgramAsync(db, programId);
        program.IsActive = false;
        await db.UpdateAsync(program);
        OnChanged();
    }

    /// <summary>Deletes a program and its templates. Workouts done with them stay in the history.</summary>
    public async Task DeleteProgramAsync(int programId)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute(
                "DELETE FROM template_exercises WHERE TemplateId IN (SELECT Id FROM templates WHERE ProgramId = ?)", programId);
            conn.Execute("DELETE FROM templates WHERE ProgramId = ?", programId);
            conn.Delete<TrainingProgram>(programId);
        });
        OnChanged();
    }

    /// <summary>Adds an empty template at the end of a program, or on its own when <paramref name="programId"/> is null.</summary>
    public async Task<WorkoutTemplate> CreateTemplateAsync(int? programId, string name, DateTime now)
    {
        var db = await GetConnectionAsync();
        if (programId is { } id)
            await FindProgramAsync(db, id);
        var position = programId is null
            ? await db.ExecuteScalarAsync<int>("SELECT IFNULL(MAX(Position), -1) + 1 FROM templates WHERE ProgramId IS NULL")
            : await db.ExecuteScalarAsync<int>("SELECT IFNULL(MAX(Position), -1) + 1 FROM templates WHERE ProgramId = ?", programId);
        var template = new WorkoutTemplate { ProgramId = programId, Name = CheckName(name), Position = position, CreatedAt = now };
        await db.InsertAsync(template);
        OnChanged();
        return template;
    }

    public async Task RenameTemplateAsync(int templateId, string name)
    {
        var db = await GetConnectionAsync();
        var template = await FindTemplateAsync(db, templateId);
        template.Name = CheckName(name);
        await db.UpdateAsync(template);
        OnChanged();
    }

    public async Task DeleteTemplateAsync(int templateId)
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM template_exercises WHERE TemplateId = ?", templateId);
            conn.Delete<WorkoutTemplate>(templateId);
        });
        OnChanged();
    }

    public async Task<TemplateOverview> GetTemplateAsync(int templateId)
    {
        var db = await GetConnectionAsync();
        return await BuildTemplateAsync(db, await FindTemplateAsync(db, templateId));
    }

    public async Task<TemplateExercise> AddExerciseToTemplateAsync(int templateId, int exerciseId, int sets, int reps)
    {
        var db = await GetConnectionAsync();
        await FindTemplateAsync(db, templateId);
        var exercise = await db.FindAsync<Exercise>(exerciseId)
            ?? throw new FonteException(FonteError.ExerciseNotFound, $"Exercise {exerciseId} does not exist.");
        var position = await db.ExecuteScalarAsync<int>(
            "SELECT IFNULL(MAX(Position), -1) + 1 FROM template_exercises WHERE TemplateId = ?", templateId);
        var item = new TemplateExercise
        {
            TemplateId = templateId,
            ExerciseId = exerciseId,
            Position = position,
            Sets = Math.Max(1, sets),
            Reps = exercise.Tracking == Tracking.Time ? 0 : Math.Max(1, reps),
            Seconds = exercise.Tracking == Tracking.Time ? 30 : 0,
        };
        await db.InsertAsync(item);
        OnChanged();
        return item;
    }

    /// <summary>
    /// Changes what an exercise of a template aims for. Like the sets of the workout in progress, this is typed in
    /// on the template's own screen and does not raise <see cref="Changed"/>.
    /// </summary>
    public async Task UpdateTemplateExerciseAsync(int itemId, int sets, int reps, double weight, int seconds)
    {
        var db = await GetConnectionAsync();
        var item = await FindTemplateExerciseAsync(db, itemId);
        item.Sets = Math.Clamp(sets, 1, 20);
        item.Reps = Math.Clamp(reps, 0, 100);
        item.Weight = Math.Clamp(Math.Round(weight, 2), 0, 1000);
        item.Seconds = Math.Clamp(seconds, 0, 6 * 3600);
        await db.UpdateAsync(item);
    }

    public async Task RemoveTemplateExerciseAsync(int itemId)
    {
        var db = await GetConnectionAsync();
        var item = await FindTemplateExerciseAsync(db, itemId);
        await db.RunInTransactionAsync(conn =>
        {
            conn.Delete(item);
            conn.Execute(
                "UPDATE template_exercises SET Position = Position - 1 WHERE TemplateId = ? AND Position > ?",
                item.TemplateId, item.Position);
        });
        OnChanged();
    }

    /// <summary>Moves an exercise one place up (<paramref name="delta"/> = -1) or down (+1).</summary>
    public async Task MoveTemplateExerciseAsync(int itemId, int delta)
    {
        var db = await GetConnectionAsync();
        var item = await FindTemplateExerciseAsync(db, itemId);
        var siblings = await db.Table<TemplateExercise>().Where(i => i.TemplateId == item.TemplateId).OrderBy(i => i.Position).ToListAsync();
        if (Swap(siblings, item.Id, delta) is { } changed)
        {
            await db.UpdateAllAsync(changed);
            OnChanged();
        }
    }

    public async Task SetTemplateExerciseLinkAsync(int itemId, bool linkedToNext)
    {
        var db = await GetConnectionAsync();
        var item = await FindTemplateExerciseAsync(db, itemId);
        item.LinkedToNext = linkedToNext;
        await db.UpdateAsync(item);
        OnChanged();
    }

    /// <summary>
    /// Starts a workout from a template: its exercises in order with the planned sets, at the planned load, or the
    /// load used last time while the template doesn't know it yet. Returns the workout in progress if there is one.
    /// </summary>
    public async Task<Workout> StartWorkoutFromTemplateAsync(int templateId, DateTime now)
    {
        if (await GetWorkoutInProgressAsync() is { } inProgress)
            return inProgress;

        var db = await GetConnectionAsync();
        var template = await FindTemplateAsync(db, templateId);
        var items = await db.Table<TemplateExercise>().Where(i => i.TemplateId == templateId).OrderBy(i => i.Position).ToListAsync();
        var done = await GetDoneSetsAsync(db);
        var workout = new Workout { StartedAt = now, TemplateId = template.Id };

        await db.RunInTransactionAsync(conn =>
        {
            conn.Insert(workout);
            for (var i = 0; i < items.Count; i++)
            {
                var target = items[i];
                var item = new WorkoutExercise
                {
                    WorkoutId = workout.Id,
                    ExerciseId = target.ExerciseId,
                    Position = i,
                    LinkedToNext = target.LinkedToNext,
                    TargetSets = target.Sets,
                    TargetReps = target.Reps > 0 ? target.Reps : null,
                    TargetWeight = target.Weight > 0 ? target.Weight : null,
                    TargetSeconds = target.Seconds > 0 ? target.Seconds : null,
                };
                conn.Insert(item);

                var previous = PreviousSets(done, target.ExerciseId, workout);
                for (var s = 0; s < Math.Max(1, target.Sets); s++)
                {
                    var last = s < previous.Count ? previous[s] : previous.LastOrDefault();
                    conn.Insert(new WorkoutSet
                    {
                        WorkoutExerciseId = item.Id,
                        Position = s,
                        Weight = target.Weight > 0 ? target.Weight : last?.Weight ?? 0,
                        Reps = target.Reps > 0 ? target.Reps : last?.Reps ?? 0,
                        Seconds = target.Seconds > 0 ? target.Seconds : last?.Seconds ?? 0,
                    });
                }
            }
        });
        OnChanged();
        return workout;
    }

    private static async Task<ProgramOverview> BuildProgramAsync(SQLiteAsyncConnection db, TrainingProgram program, DateTime today)
    {
        var templates = await db.Table<WorkoutTemplate>().Where(t => t.ProgramId == program.Id).OrderBy(t => t.Position).ToListAsync();
        var overviews = new List<TemplateOverview>();
        foreach (var template in templates)
            overviews.Add(await BuildTemplateAsync(db, template));

        // Templates are done in turn: the next one follows the last one done.
        var ids = templates.Select(t => t.Id).ToList();
        var lastDone = (await db.Table<Workout>().Where(w => w.FinishedAt != null && w.TemplateId != null).ToListAsync())
            .Where(w => ids.Contains(w.TemplateId!.Value))
            .MaxBy(w => w.StartedAt);
        var next = overviews.Count == 0
            ? null
            : lastDone is null
                ? overviews[0]
                : overviews[(ids.IndexOf(lastDone.TemplateId!.Value) + 1) % overviews.Count];

        var week = program.IsActive ? (int)((today.Date - program.StartedOn.Date).TotalDays / 7) + 1 : 1;
        if (program.Weeks > 0)
            week = Math.Min(week, program.Weeks);
        return new ProgramOverview(program, overviews, Math.Max(1, week), next);
    }

    private static async Task<TemplateOverview> BuildTemplateAsync(SQLiteAsyncConnection db, WorkoutTemplate template)
    {
        var items = await db.Table<TemplateExercise>().Where(i => i.TemplateId == template.Id).OrderBy(i => i.Position).ToListAsync();
        var entries = new List<TemplateEntry>();
        foreach (var item in items)
        {
            if (await db.FindAsync<Exercise>(item.ExerciseId) is { } exercise)
                entries.Add(new TemplateEntry(item, exercise));
        }
        var templateId = template.Id;
        var last = await db.Table<Workout>()
            .Where(w => w.TemplateId == templateId && w.FinishedAt != null)
            .OrderByDescending(w => w.StartedAt)
            .FirstOrDefaultAsync();
        return new TemplateOverview(template, entries) { LastDone = last?.StartedAt };
    }

    /// <summary>Swaps an item with its neighbour and renumbers; null when it is already at that end.</summary>
    private static IReadOnlyList<T>? Swap<T>(List<T> ordered, int id, int delta) where T : class
    {
        var index = ordered.FindIndex(i => GetId(i) == id);
        var other = index + Math.Sign(delta);
        if (index < 0 || other < 0 || other >= ordered.Count)
            return null;
        (ordered[index], ordered[other]) = (ordered[other], ordered[index]);
        for (var i = 0; i < ordered.Count; i++)
            SetPosition(ordered[i], i);
        return ordered;
    }

    private static int GetId(object item) => item switch
    {
        TemplateExercise t => t.Id,
        WorkoutExercise w => w.Id,
        _ => throw new ArgumentException("Unexpected item.", nameof(item)),
    };

    private static void SetPosition(object item, int position)
    {
        switch (item)
        {
            case TemplateExercise t:
                t.Position = position;
                break;
            case WorkoutExercise w:
                w.Position = position;
                break;
        }
    }

    private static string CheckName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new FonteException(FonteError.EmptyName, "A name is needed.");
        return trimmed.Length > 60 ? trimmed[..60] : trimmed;
    }

    private static async Task<TrainingProgram> FindProgramAsync(SQLiteAsyncConnection db, int programId) =>
        await db.FindAsync<TrainingProgram>(programId)
            ?? throw new FonteException(FonteError.ProgramNotFound, $"Program {programId} does not exist.");

    private static async Task<WorkoutTemplate> FindTemplateAsync(SQLiteAsyncConnection db, int templateId) =>
        await db.FindAsync<WorkoutTemplate>(templateId)
            ?? throw new FonteException(FonteError.TemplateNotFound, $"Template {templateId} does not exist.");

    private static async Task<TemplateExercise> FindTemplateExerciseAsync(SQLiteAsyncConnection db, int itemId) =>
        await db.FindAsync<TemplateExercise>(itemId)
            ?? throw new FonteException(FonteError.TemplateNotFound, $"Template exercise {itemId} does not exist.");
}
