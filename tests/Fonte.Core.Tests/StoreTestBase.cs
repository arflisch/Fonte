using Fonte.Core.Data;
using Fonte.Core.Training;

namespace Fonte.Core.Tests;

/// <summary>A store on a real SQLite file, deleted after each test.</summary>
public abstract class StoreTestBase : IAsyncLifetime
{
    protected static readonly DateTime Monday = new(2026, 9, 28, 18, 0, 0);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fonte-{Guid.NewGuid():N}.db3");

    protected FonteStore Store { get; private set; } = null!;

    public Task InitializeAsync()
    {
        Store = new FonteStore(_path);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Store.DisposeAsync();
        foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
            File.Delete(file);
    }

    protected async Task<int> IdOf(string catalogKey) =>
        (await Store.GetExercisesAsync()).Single(e => e.CatalogKey == catalogKey).Id;

    /// <summary>A finished workout where every listed set was ticked.</summary>
    protected async Task<WorkoutSummary> DoWorkoutAsync(DateTime start, params (int ExerciseId, (double Weight, int Reps)[] Sets)[] exercises)
    {
        var workout = await Store.StartWorkoutAsync(start);
        foreach (var (exerciseId, sets) in exercises)
        {
            var item = await Store.AddExerciseToWorkoutAsync(workout.Id, exerciseId);
            var existing = (await Store.GetWorkoutDetailAsync(workout.Id)).Entries.Single(e => e.Item.Id == item.Id).Sets;
            for (var i = 0; i < sets.Length; i++)
            {
                var id = i < existing.Count ? existing[i].Id : (await Store.AddSetAsync(item.Id)).Id;
                await Store.UpdateSetAsync(id, sets[i].Weight, sets[i].Reps, 0);
                await Store.SetDoneAsync(id, true, start);
            }
        }
        return (await Store.FinishWorkoutAsync(workout.Id, start.AddMinutes(45)))!;
    }

    /// <summary>Starts a template workout, ticks its prefilled sets after applying <paramref name="edit"/>, and finishes it.</summary>
    protected async Task<WorkoutSummary> DoTemplateWorkoutAsync(int templateId, DateTime start, Func<WorkoutSetEdit, (double Weight, int Reps, int Seconds)>? edit = null)
    {
        var workout = await Store.StartWorkoutFromTemplateAsync(templateId, start);
        foreach (var entry in (await Store.GetWorkoutDetailAsync(workout.Id)).Entries)
        {
            foreach (var set in entry.Sets)
            {
                if (edit is not null)
                {
                    var (weight, reps, seconds) = edit(new WorkoutSetEdit(entry.Exercise.CatalogKey, set.Position, set.Weight, set.Reps, set.Seconds));
                    await Store.UpdateSetAsync(set.Id, weight, reps, seconds);
                }
                await Store.SetDoneAsync(set.Id, true, start);
            }
        }
        return (await Store.FinishWorkoutAsync(workout.Id, start.AddMinutes(50)))!;
    }

    protected sealed record WorkoutSetEdit(string? Exercise, int Position, double Weight, int Reps, int Seconds);
}
