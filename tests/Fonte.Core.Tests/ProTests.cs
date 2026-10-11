using System.Globalization;
using System.Text;
using Fonte.Core.Catalog;
using Fonte.Core.Models;
using Fonte.Core.Training;

namespace Fonte.Core.Tests;

/// <summary>What Fonte Pro relies on: set kinds, RPE, rest per exercise, optional progression, CSV export.</summary>
public sealed class ProTests : StoreTestBase
{
    [Fact]
    public async Task Warm_ups_count_in_no_record_volume_or_set_count()
    {
        var bench = await IdOf("bench_press");
        await DoWorkoutAsync(Monday, (bench, [(80, 5)]));
        var workout = await Store.StartWorkoutAsync(Monday.AddDays(2));
        var item = await Store.AddExerciseToWorkoutAsync(workout.Id, bench);
        // A heavy warm-up would be a record if it counted.
        var warmUp = (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets[0];
        await Store.UpdateSetAsync(warmUp.Id, 100, 5, 0);
        await Store.SetSetKindAsync(warmUp.Id, SetKind.WarmUp);
        await Store.SetDoneAsync(warmUp.Id, true, Monday);
        var working = await Store.AddSetAsync(item.Id);
        await Store.UpdateSetAsync(working.Id, 80, 5, 0);
        await Store.SetDoneAsync(working.Id, true, Monday);

        var summary = (await Store.FinishWorkoutAsync(workout.Id, Monday.AddDays(2).AddHours(1)))!;

        Assert.Empty(summary.Records);
        Assert.Equal((400d, 1), (summary.Volume, summary.SetCount));
        Assert.Equal(80, (await Store.GetExerciseHistoryAsync(bench)).Best!.Weight);
        Assert.Equal(item.Id, summary.Exercises[0].Sets[0].WorkoutExerciseId);
    }

    [Fact]
    public async Task Drop_sets_and_warm_ups_do_not_hold_back_the_progression()
    {
        var squat = await IdOf("squat");
        var template = await Store.CreateTemplateAsync(null, "Legs", Monday);
        var target = await Store.AddExerciseToTemplateAsync(template.Id, squat, 2, 5);
        await Store.UpdateTemplateExerciseAsync(target.Id, 2, 5, 100, 0);
        var workout = await Store.StartWorkoutFromTemplateAsync(template.Id, Monday);
        var entry = (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0];
        var warmUp = await Store.AddSetAsync(entry.Item.Id);
        var drop = await Store.AddSetAsync(entry.Item.Id);
        await Store.UpdateSetAsync(warmUp.Id, 40, 8, 0);
        await Store.SetSetKindAsync(warmUp.Id, SetKind.WarmUp);
        await Store.UpdateSetAsync(drop.Id, 70, 6, 0);
        await Store.SetSetKindAsync(drop.Id, SetKind.Drop);
        foreach (var set in (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets)
            await Store.SetDoneAsync(set.Id, true, Monday);

        var summary = (await Store.FinishWorkoutAsync(workout.Id, Monday.AddHours(1)))!;

        Assert.Equal(102.5, Assert.Single(summary.Progressions).To);
        Assert.True(summary.ProgressionsApplied);
    }

    [Fact]
    public async Task Without_pro_the_progression_is_only_worked_out()
    {
        var bench = await IdOf("bench_press");
        var template = await Store.CreateTemplateAsync(null, "Push", Monday);
        var target = await Store.AddExerciseToTemplateAsync(template.Id, bench, 2, 5);
        await Store.UpdateTemplateExerciseAsync(target.Id, 2, 5, 60, 0);
        var workout = await Store.StartWorkoutFromTemplateAsync(template.Id, Monday);
        foreach (var set in (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets)
            await Store.SetDoneAsync(set.Id, true, Monday);

        var summary = (await Store.FinishWorkoutAsync(workout.Id, Monday.AddHours(1), applyProgression: false))!;

        Assert.Equal(62.5, Assert.Single(summary.Progressions).To);
        Assert.False(summary.ProgressionsApplied);
        Assert.Equal(60, (await Store.GetTemplateAsync(template.Id)).Entries[0].Item.Weight);
    }

    [Fact]
    public async Task The_rest_of_an_exercise_goes_from_the_template_to_the_workout()
    {
        var template = await Store.CreateTemplateAsync(null, "Legs", Monday);
        var target = await Store.AddExerciseToTemplateAsync(template.Id, await IdOf("squat"), 3, 5);
        await Store.SetTemplateExerciseRestAsync(target.Id, 180);

        var workout = await Store.StartWorkoutFromTemplateAsync(template.Id, Monday);
        var item = (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Item;
        Assert.Equal(180, item.RestSeconds);

        await Store.SetWorkoutExerciseRestAsync(item.Id, null);
        Assert.Null((await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Item.RestSeconds);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(11)]
    [InlineData(7.3)]
    public async Task An_rpe_goes_from_1_to_10_by_half_points(double rpe)
    {
        var workout = await Store.StartWorkoutAsync(Monday);
        await Store.AddExerciseToWorkoutAsync(workout.Id, await IdOf("squat"));
        var set = (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets[0];

        await Store.SetSetRpeAsync(set.Id, 8.5);
        var error = await Assert.ThrowsAsync<FonteException>(() => Store.SetSetRpeAsync(set.Id, rpe));

        Assert.Equal(FonteError.InvalidRpe, error.Error);
        Assert.Equal(8.5, (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets[0].Rpe);
    }

    [Fact]
    public async Task Only_the_user_s_own_workouts_are_counted()
    {
        await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Suggest(3), key => key, Monday);
        await Store.CreateTemplateAsync(null, "Arms", Monday);
        var program = await Store.CreateProgramAsync("Mine", Monday);
        await Store.CreateTemplateAsync(program.Id, "A", Monday);

        Assert.Equal(2, await Store.CountOwnTemplatesAsync());
    }

    [Fact]
    public async Task The_csv_has_one_line_per_set_in_the_user_s_format()
    {
        var bench = await IdOf("bench_press");
        var plank = await IdOf("plank");
        var summary = await DoWorkoutAsync(Monday, (bench, [(82.5, 5), (80, 6)]));
        await Store.SaveWorkoutReviewAsync(summary.Workout.Id, 4, "Bonne; séance");
        var workout = await Store.StartWorkoutAsync(Monday.AddDays(1));
        await Store.AddExerciseToWorkoutAsync(workout.Id, plank);
        var set = (await Store.GetWorkoutDetailAsync(workout.Id)).Entries[0].Sets[0];
        await Store.UpdateSetAsync(set.Id, 0, 0, 45);
        await Store.SetSetRpeAsync(set.Id, 8);
        await Store.SetDoneAsync(set.Id, true, Monday);
        await Store.FinishWorkoutAsync(workout.Id, Monday.AddDays(1).AddMinutes(10));

        var texts = new CsvTexts("Date", "Heure", "Séance", "Exercice", "Groupe", "Série", "Type", "Charge", "Rép.", "Secondes", "RPE", "Note",
            e => e.CatalogKey!, m => m.ToString(), k => k.ToString());
        var csv = Encoding.UTF8.GetString(WorkoutCsv.Write(await Store.GetRecentWorkoutsAsync(int.MaxValue), texts, CultureInfo.GetCultureInfo("fr-FR")));

        var lines = csv.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
        [
            "Date;Heure;Séance;Exercice;Groupe;Série;Type;Charge;Rép.;Secondes;RPE;Note",
            "2026-09-28;18:00;;bench_press;Chest;1;Normal;82,5;5;;;\"Bonne; séance\"",
            "2026-09-28;18:00;;bench_press;Chest;2;Normal;80;6;;;",
            "2026-09-29;18:00;;plank;Core;1;Normal;;;45;8;",
        ], lines);
    }
}
