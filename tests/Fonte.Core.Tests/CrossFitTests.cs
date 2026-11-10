using Fonte.Core.Catalog;
using Fonte.Core.Models;

namespace Fonte.Core.Tests;

public sealed class CrossFitTests : StoreTestBase
{
    private static readonly DateTime Now = new(2026, 11, 10, 18, 0, 0);

    [Fact]
    public void Crossfit_programs_end_each_workout_with_a_wod_of_known_movements()
    {
        var crossfit = ProgramCatalog.All.Where(p => p.Sport == TrainingSport.CrossFit).ToList();
        Assert.Equal(2, crossfit.Count);
        var keys = ExerciseCatalog.All.Select(e => e.Key).ToHashSet();
        Assert.All(crossfit.SelectMany(p => p.Templates), t =>
        {
            Assert.NotNull(t.Wod);
            Assert.Contains(t.Exercises, e => e.IsWod);
            Assert.All(t.Exercises, e => Assert.Contains(e.ExerciseKey, keys));
            // The WOD movements come last, chained together.
            var wod = t.Exercises.SkipWhile(e => !e.IsWod).ToList();
            Assert.All(wod, e => Assert.True(e.IsWod));
            Assert.All(wod.SkipLast(1), e => Assert.True(e.LinkedToNext));
        });
        Assert.Equal("crossfit_foundations", ProgramCatalog.Suggest(TrainingSport.CrossFit, TrainingGoal.Muscle, 3).Key);
        Assert.Equal("crossfit_performance", ProgramCatalog.Suggest(TrainingSport.CrossFit, TrainingGoal.Muscle, 5).Key);
        Assert.Equal("full_body", ProgramCatalog.Suggest(TrainingSport.Strength, TrainingGoal.Muscle, 3).Key);
    }

    [Fact]
    public async Task A_crossfit_workout_carries_its_wod_and_records_its_score()
    {
        await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Find("crossfit_foundations"), k => k, Now);
        var program = (await Store.GetActiveProgramAsync(Now))!;
        var template = program.Templates[0].Template;
        Assert.Equal((WodFormat.Amrap, 12), (template.WodFormat, template.WodMinutes));

        var workout = await Store.StartWorkoutFromTemplateAsync(template.Id, Now);
        Assert.Equal((WodFormat.Amrap, 12), (workout.WodFormat, workout.WodMinutes));
        var detail = await Store.GetWorkoutDetailAsync(workout.Id);
        Assert.Equal([false, true, true, true], detail.Entries.Select(e => e.Item.IsWod));

        await Store.SaveWodScoreAsync(workout.Id, WodFormat.Amrap, 12, 7, 14, 0);
        var saved = (await Store.GetWorkoutDetailAsync(workout.Id)).Workout;
        Assert.True(saved.HasWodScore);
        Assert.Equal((7, 14), (saved.WodRounds, saved.WodReps));
    }

    [Fact]
    public async Task Wod_movements_are_not_raised_by_the_automatic_progression()
    {
        await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Find("crossfit_foundations"), k => k, Now);
        var template = (await Store.GetActiveProgramAsync(Now))!.Templates[0].Template;
        var workout = await Store.StartWorkoutFromTemplateAsync(template.Id, Now);
        foreach (var entry in (await Store.GetWorkoutDetailAsync(workout.Id)).Entries)
        {
            foreach (var set in entry.Sets)
            {
                await Store.UpdateSetAsync(set.Id, 40, 20, 0);
                await Store.SetDoneAsync(set.Id, true, Now);
            }
        }

        var summary = (await Store.FinishWorkoutAsync(workout.Id, Now.AddHours(1)))!;

        Assert.All(summary.Progressions, p => Assert.Equal("squat", p.Exercise.CatalogKey));
    }
}
