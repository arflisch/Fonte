using Fonte.Core.Catalog;
using Fonte.Core.Models;
using Fonte.Core.Training;

namespace Fonte.Core.Tests;

public sealed class ProgramTests : StoreTestBase
{
    [Fact]
    public async Task A_ready_made_program_comes_with_its_templates_and_is_followed()
    {
        await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Suggest(3), key => key, Monday);

        var program = (await Store.GetActiveProgramAsync(Monday))!;
        Assert.Equal(("Program_full_body", 8, 1), (program.Program.Name, program.Program.Weeks, program.Week));
        Assert.Equal(["Template_full_body_a", "Template_full_body_b"], program.Templates.Select(t => t.Template.Name));
        Assert.Equal(5, program.Templates[0].Entries.Count);
        Assert.Equal(program.Templates[0].Template.Id, program.Next!.Template.Id);
    }

    [Fact]
    public async Task Every_ready_made_exercise_exists_in_the_library()
    {
        var keys = ExerciseCatalog.All.Select(e => e.Key).ToHashSet();

        Assert.All(ProgramCatalog.All.SelectMany(p => p.Templates).SelectMany(t => t.Exercises), e => Assert.Contains(e.ExerciseKey, keys));
        foreach (var catalog in ProgramCatalog.All)
        {
            await Store.CreateProgramFromCatalogAsync(catalog, key => key, Monday);
            var program = (await Store.GetActiveProgramAsync(Monday))!;
            Assert.Equal(catalog.Templates.Sum(t => t.Exercises.Count), program.Templates.Sum(t => t.Entries.Count));
        }
    }

    [Fact]
    public async Task Only_one_program_is_followed_at_a_time()
    {
        var first = await Store.CreateProgramAsync("Mine", Monday);
        var second = await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Suggest(4), key => key, Monday);

        Assert.Equal(second.Id, (await Store.GetActiveProgramAsync(Monday))!.Program.Id);

        await Store.ActivateProgramAsync(first.Id, Monday.AddDays(1));
        var programs = await Store.GetProgramsAsync(Monday);
        Assert.Equal([first.Id, second.Id], programs.Select(p => p.Program.Id));
        Assert.Equal([true, false], programs.Select(p => p.Program.IsActive));

        await Store.StopProgramAsync(first.Id);
        Assert.Null(await Store.GetActiveProgramAsync(Monday));
    }

    [Fact]
    public async Task Templates_are_done_in_turn_and_weeks_count_from_the_start()
    {
        await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Suggest(3), key => key, Monday);
        var program = (await Store.GetActiveProgramAsync(Monday))!;
        var (a, b) = (program.Templates[0].Template.Id, program.Templates[1].Template.Id);

        await DoTemplateWorkoutAsync(a, Monday, Done);
        var afterA = (await Store.GetActiveProgramAsync(Monday))!;
        Assert.Equal(b, afterA.Next!.Template.Id);
        Assert.Equal([Monday, null], afterA.Templates.Select(t => t.LastDone));

        await DoTemplateWorkoutAsync(b, Monday.AddDays(2), Done);
        var later = (await Store.GetActiveProgramAsync(Monday.AddDays(15)))!;
        Assert.Equal(a, later.Next!.Template.Id);
        Assert.Equal(3, later.Week);
        Assert.Equal(8, (await Store.GetActiveProgramAsync(Monday.AddDays(200)))!.Week);
    }

    [Fact]
    public async Task A_template_workout_has_the_planned_sets_with_last_time_s_loads()
    {
        var bench = await IdOf("bench_press");
        await DoWorkoutAsync(Monday, (bench, [(80, 8), (77.5, 8)]));
        var template = await Store.CreateTemplateAsync(null, "Push", Monday);
        await Store.AddExerciseToTemplateAsync(template.Id, bench, 3, 8);

        var workout = await Store.StartWorkoutFromTemplateAsync(template.Id, Monday.AddDays(2));

        var entry = Assert.Single((await Store.GetWorkoutDetailAsync(workout.Id)).Entries);
        Assert.Equal(template.Id, workout.TemplateId);
        Assert.Equal((3, 8, (double?)null), (entry.Item.TargetSets, entry.Item.TargetReps, entry.Item.TargetWeight));
        Assert.Equal([(80d, 8), (77.5, 8), (77.5, 8)], entry.Sets.Select(s => (s.Weight, s.Reps)));
        Assert.Equal(workout.Id, (await Store.StartWorkoutFromTemplateAsync(template.Id, Monday.AddDays(2))).Id);
    }

    [Fact]
    public async Task The_load_goes_up_once_every_planned_set_succeeds()
    {
        var bench = await IdOf("bench_press");
        var template = await Store.CreateTemplateAsync(null, "Push", Monday);
        var item = await Store.AddExerciseToTemplateAsync(template.Id, bench, 3, 5);
        await Store.UpdateTemplateExerciseAsync(item.Id, 3, 5, 60, 0);

        var success = await DoTemplateWorkoutAsync(template.Id, Monday);
        var step = Assert.Single(success.Progressions);
        Assert.Equal((ProgressionKind.Weight, 60d, 62.5), (step.Kind, step.From, step.To));
        Assert.Equal(62.5, await TargetWeightAsync(template.Id));

        var missed = await DoTemplateWorkoutAsync(template.Id, Monday.AddDays(2), s => (s.Weight, s.Position == 2 ? 4 : s.Reps, 0));
        Assert.Empty(missed.Progressions);
        Assert.Equal(62.5, await TargetWeightAsync(template.Id));
    }

    [Fact]
    public async Task A_template_without_a_load_takes_the_first_one_used()
    {
        var squat = await IdOf("squat");
        var template = await Store.CreateTemplateAsync(null, "Legs", Monday);
        await Store.AddExerciseToTemplateAsync(template.Id, squat, 3, 5);

        var summary = await DoTemplateWorkoutAsync(template.Id, Monday, s => (100, s.Reps, 0));

        Assert.Empty(summary.Progressions);
        Assert.Equal(100, await TargetWeightAsync(template.Id));
    }

    [Fact]
    public async Task Template_exercises_can_be_reordered_and_linked()
    {
        var template = await Store.CreateTemplateAsync(null, "Arms", Monday);
        var curl = await Store.AddExerciseToTemplateAsync(template.Id, await IdOf("barbell_curl"), 3, 10);
        var pushdown = await Store.AddExerciseToTemplateAsync(template.Id, await IdOf("triceps_pushdown"), 3, 12);
        var plank = await Store.AddExerciseToTemplateAsync(template.Id, await IdOf("plank"), 3, 0);

        await Store.MoveTemplateExerciseAsync(plank.Id, -1);
        await Store.MoveTemplateExerciseAsync(curl.Id, -1);
        await Store.SetTemplateExerciseLinkAsync(curl.Id, true);
        await Store.RemoveTemplateExerciseAsync(pushdown.Id);

        var entries = (await Store.GetTemplateAsync(template.Id)).Entries;
        Assert.Equal([curl.Id, plank.Id], entries.Select(e => e.Item.Id));
        Assert.Equal([0, 1], entries.Select(e => e.Item.Position));
        Assert.True(entries[0].Item.LinkedToNext);
        Assert.Equal((0, 30), (entries[1].Item.Reps, entries[1].Item.Seconds));
    }

    [Fact]
    public async Task Deleting_a_program_keeps_its_workouts()
    {
        var program = await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Suggest(3), key => key, Monday);
        var template = (await Store.GetActiveProgramAsync(Monday))!.Templates[0].Template;
        var summary = await DoTemplateWorkoutAsync(template.Id, Monday, Done);

        await Store.DeleteProgramAsync(program.Id);

        Assert.Empty(await Store.GetProgramsAsync(Monday));
        var kept = (await Store.GetWorkoutSummaryAsync(summary.Workout.Id))!;
        Assert.Null(kept.TemplateName);
        Assert.NotEmpty(kept.Exercises);
    }

    [Fact]
    public async Task Names_are_required()
    {
        var error = await Assert.ThrowsAsync<FonteException>(() => Store.CreateProgramAsync("  ", Monday));
        Assert.Equal(FonteError.EmptyName, error.Error);
    }

    [Fact]
    public async Task The_summary_compares_with_the_previous_workout_of_the_template()
    {
        var bench = await IdOf("bench_press");
        var template = await Store.CreateTemplateAsync(null, "Push", Monday);
        await Store.AddExerciseToTemplateAsync(template.Id, bench, 2, 5);
        await DoWorkoutAsync(Monday.AddDays(-1), (await IdOf("squat"), [(100, 5)]));

        var first = await DoTemplateWorkoutAsync(template.Id, Monday, s => (100, 5, 0));
        var second = await DoTemplateWorkoutAsync(template.Id, Monday.AddDays(3), s => (110, 5, 0));

        Assert.Equal("Push", first.TemplateName);
        Assert.Null(first.Previous);
        Assert.Equal(first.Workout.Id, second.Previous!.Workout.Id);
        Assert.Equal((1000d, 2), (second.Previous.Volume, second.Previous.SetCount));
        Assert.Equal(1100, second.Volume);
    }

    [Fact]
    public async Task The_workout_in_progress_can_be_reordered_and_linked()
    {
        var workout = await Store.StartWorkoutAsync(Monday);
        var first = await Store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("squat"));
        var second = await Store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("leg_curl"));

        await Store.MoveWorkoutExerciseAsync(second.Id, -1);
        await Store.SetWorkoutExerciseLinkAsync(second.Id, true);

        var entries = (await Store.GetWorkoutDetailAsync(workout.Id)).Entries;
        Assert.Equal([second.Id, first.Id], entries.Select(e => e.Item.Id));
        Assert.True(entries[0].Item.LinkedToNext);
    }

    [Theory]
    [InlineData(TrainingGoal.Strength, 2, "five_by_five")]
    [InlineData(TrainingGoal.Strength, 4, "upper_lower")]
    [InlineData(TrainingGoal.Muscle, 3, "full_body")]
    [InlineData(TrainingGoal.Fitness, 6, "push_pull_legs")]
    public void The_suggested_program_depends_on_the_goal_and_the_week(TrainingGoal goal, int sessions, string expected)
    {
        Assert.Equal(expected, ProgramCatalog.Suggest(goal, sessions).Key);
    }

    [Fact]
    public void Repetitions_follow_the_goal()
    {
        var reps = (TrainingGoal goal) => ProgramCatalog.Suggest(goal, 4).Templates[0].Exercises.Select(e => e.Reps).ToList();

        Assert.Equal([6, 6, 8, 10, 10, 12], reps(TrainingGoal.Muscle));
        Assert.Equal([5, 5, 5, 10, 10, 12], reps(TrainingGoal.Strength));
        Assert.Equal([12, 12, 12, 12, 12, 12], reps(TrainingGoal.Fitness));
        Assert.Equal(0, ProgramCatalog.Suggest(TrainingGoal.Fitness, 3).Templates[0].Exercises[^1].Reps); // the plank is timed
    }

    private static (double, int, int) Done(WorkoutSetEdit set) =>
        (set.Weight > 0 ? set.Weight : 40, set.Reps > 0 ? set.Reps : 8, set.Seconds);

    private async Task<double> TargetWeightAsync(int templateId) =>
        (await Store.GetTemplateAsync(templateId)).Entries[0].Item.Weight;
}
