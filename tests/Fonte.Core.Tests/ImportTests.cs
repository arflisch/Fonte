using Fonte.Core.Models;
using Fonte.Core.Training;

namespace Fonte.Core.Tests;

public sealed class ImportTests : StoreTestBase
{
    // Strong's classic export: the unit is not written, durations read "1h 5m".
    private const string Strong = """
        Date,Workout Name,Duration,Exercise Name,Set Order,Weight,Reps,Distance,Seconds,Notes,Workout Notes,RPE
        2024-01-15 08:30:00,"Push, heavy",1h 5m,Bench Press (Barbell),W,40,10,0,0,,Felt good,
        2024-01-15 08:30:00,"Push, heavy",1h 5m,Bench Press (Barbell),1,80,5,0,0,,Felt good,8
        2024-01-15 08:30:00,"Push, heavy",1h 5m,Bench Press (Barbell),2,80,5,0,0,,Felt good,8.5
        2024-01-15 08:30:00,"Push, heavy",1h 5m,Cable Fly Low To High,1,15,12,0,0,,Felt good,
        2024-01-15 08:30:00,"Push, heavy",1h 5m,Plank,1,0,0,0,60,,Felt good,
        2024-01-17 18:00:00,Legs,45m,Squat (Barbell),1,100,5,0,0,,,
        """;

    // Recent Strong: semicolons, the unit in the header, rest timers as lines of their own.
    private const string StrongRecent = """
        Workout #;Date;Workout Name;Duration (sec);Exercise Name;Set Order;Weight (lbs);Reps;RPE;Distance (meters);Seconds;Notes;Workout Notes
        1;2024-02-01 07:00:00;Upper;3600;Deadlift (Barbell);1;225;5;;;;;
        1;2024-02-01 07:00:00;Upper;3600;Deadlift (Barbell);Rest Timer;;;;;120;;
        """;

    private const string Hevy = """
        "title","start_time","end_time","description","exercise_title","superset_id","exercise_notes","set_index","set_type","weight_kg","reps","distance_km","duration_seconds","rpe"
        "Pull","15 Jan 2024, 18:00","15 Jan 2024, 19:10","","Lat Pulldown (Cable)","0","","0","warmup","30","12","","",""
        "Pull","15 Jan 2024, 18:00","15 Jan 2024, 19:10","","Lat Pulldown (Cable)","0","","1","normal","55","10","","","9"
        "Pull","15 Jan 2024, 18:00","15 Jan 2024, 19:10","","Bicep Curl (Dumbbell)","0","","0","dropset","12","8","","",""
        "Pull","15 Jan 2024, 18:00","15 Jan 2024, 19:10","","Treadmill","","","0","normal","","","2","900",""
        """;

    [Fact]
    public void A_strong_export_is_read_with_warm_ups_rpe_and_duration()
    {
        var file = WorkoutImport.Read(Strong, pounds: false)!;

        Assert.Equal(ImportSource.Strong, file.Source);
        Assert.False(file.UnitKnown);
        Assert.Equal(2, file.Workouts.Count);
        var push = file.Workouts[0];
        Assert.Equal(("Push, heavy", "Felt good"), (push.Title, push.Note));
        Assert.Equal(TimeSpan.FromMinutes(65), push.FinishedAt - push.StartedAt);
        Assert.Equal(["Bench Press (Barbell)", "Cable Fly Low To High", "Plank"], push.Exercises.Select(e => e.Name));
        var bench = push.Exercises[0].Sets;
        Assert.Equal([SetKind.WarmUp, SetKind.Normal, SetKind.Normal], bench.Select(s => s.Kind));
        Assert.Equal((80, 5, 8.5), (bench[2].Weight, bench[2].Reps, bench[2].Rpe!.Value));
        Assert.Equal(60, push.Exercises[2].Sets[0].Seconds);
    }

    [Fact]
    public void Loads_in_pounds_are_converted_to_kilograms()
    {
        var classic = WorkoutImport.Read(Strong, pounds: true)!;
        Assert.Equal(36.287, classic.Workouts[0].Exercises[0].Sets[1].Weight, 3); // 80 lb

        var recent = WorkoutImport.Read(StrongRecent, pounds: false)!;
        Assert.True(recent.UnitKnown && recent.WeightsInPounds);
        var deadlift = Assert.Single(Assert.Single(recent.Workouts).Exercises);
        Assert.Equal(102.058, Assert.Single(deadlift.Sets).Weight, 3); // 225 lb, the rest timer dropped
        Assert.Equal(TimeSpan.FromHours(1), recent.Workouts[0].FinishedAt - recent.Workouts[0].StartedAt);
    }

    [Fact]
    public void A_hevy_export_is_read_with_set_types_and_supersets()
    {
        var file = WorkoutImport.Read(Hevy, pounds: true)!;

        Assert.Equal(ImportSource.Hevy, file.Source);
        Assert.False(file.WeightsInPounds);
        var pull = Assert.Single(file.Workouts);
        Assert.Equal(new DateTime(2024, 1, 15, 18, 0, 0), pull.StartedAt);
        Assert.Equal(new DateTime(2024, 1, 15, 19, 10, 0), pull.FinishedAt);
        Assert.Equal([SetKind.WarmUp, SetKind.Normal], pull.Exercises[0].Sets.Select(s => s.Kind));
        Assert.Equal(9, pull.Exercises[0].Sets[1].Rpe);
        Assert.Equal(SetKind.Drop, pull.Exercises[1].Sets[0].Kind);
        Assert.Equal(("0", "0"), (pull.Exercises[0].SupersetId, pull.Exercises[1].SupersetId));
        Assert.Equal(900, pull.Exercises[2].Sets[0].Seconds);
    }

    [Fact]
    public void Other_files_are_not_taken_for_an_export()
    {
        Assert.Null(WorkoutImport.Read("name,amount\nrent,800", pounds: false));
        Assert.Null(WorkoutImport.Read(string.Empty, pounds: false));
    }

    [Fact]
    public void Exercises_are_matched_to_the_built_in_ones_or_guessed()
    {
        var matches = WorkoutImport.Match(WorkoutImport.Read(Strong + "\n" + Hevy.Split('\n', 2)[1], false)!.Workouts)
            .ToDictionary(m => m.Name);
        Assert.Equal("bench_press", matches["Bench Press (Barbell)"].CatalogKey);
        Assert.Equal("plank", matches["Plank"].CatalogKey);
        Assert.Equal("squat", matches["Squat (Barbell)"].CatalogKey);

        var fly = matches["Cable Fly Low To High"];
        Assert.Null(fly.CatalogKey);
        Assert.Equal((MuscleGroup.Chest, Equipment.Cable, Tracking.WeightAndReps), (fly.Muscle, fly.Equipment, fly.Tracking));
    }

    [Fact]
    public async Task Importing_adds_finished_workouts_once_and_creates_the_unknown_exercises()
    {
        var file = WorkoutImport.Read(Strong, pounds: false)!;

        var preview = await Store.PreviewImportAsync(file.Workouts);
        Assert.Equal((2, 2, 6), (preview.Workouts, preview.NewWorkouts, preview.Sets));
        Assert.Equal(["Cable Fly Low To High"], preview.NewExercises);

        var result = await Store.ImportAsync(file.Workouts);
        Assert.Equal(new Data.ImportResult(2, 6, 1), result);

        var recent = await Store.GetRecentWorkoutsAsync(10);
        Assert.Equal(2, recent.Count);
        var push = recent.Single(w => w.Workout.StartedAt.Day == 15);
        Assert.Equal("Push, heavy · Felt good", push.Workout.Note);
        Assert.Equal(3, push.Exercises.Count);
        var bench = await Store.GetExerciseHistoryAsync(push.Exercises[0].Exercise.Id);
        Assert.Equal(80, bench.Best!.Weight);

        // The same file again adds nothing, and reuses the exercise it created.
        Assert.Equal(0, (await Store.PreviewImportAsync(file.Workouts)).NewWorkouts);
        Assert.Equal(new Data.ImportResult(0, 0, 0), await Store.ImportAsync(file.Workouts));
        Assert.Single(await Store.GetExercisesAsync(), e => e.CustomName == "Cable Fly Low To High");
    }

    [Fact]
    public async Task Hevy_supersets_are_linked()
    {
        await Store.ImportAsync(WorkoutImport.Read(Hevy, pounds: false)!.Workouts);

        var pull = Assert.Single(await Store.GetRecentWorkoutsAsync(1));
        var detail = await Store.GetWorkoutDetailAsync(pull.Workout.Id);
        Assert.Equal([true, false, false], detail.Entries.Select(e => e.Item.LinkedToNext));
    }
}
