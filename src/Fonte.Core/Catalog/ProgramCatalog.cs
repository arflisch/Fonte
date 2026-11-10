using Fonte.Core.Models;

namespace Fonte.Core.Catalog;

/// <summary>What the user trains for, chosen when the app is first opened: it shapes the repetitions and rests.</summary>
public enum TrainingGoal
{
    /// <summary>Heavy loads: 3 to 6 repetitions, long rests.</summary>
    Strength = 0,

    /// <summary>Muscle growth: 8 to 12 repetitions, 90 s rests.</summary>
    Muscle = 1,

    /// <summary>General fitness: 12 to 15 repetitions, short rests.</summary>
    Fitness = 2,
}

/// <summary>The kind of training the user does: it decides the ready-made programs offered first.</summary>
public enum TrainingSport
{
    Strength = 0,
    CrossFit = 1,
}

/// <param name="ExerciseKey">Key of a built-in exercise (<see cref="ExerciseCatalog"/>).</param>
/// <param name="Seconds">Duration to hold, for timed exercises (then <paramref name="Reps"/> is 0).</param>
/// <param name="IsWod">A movement of the template's WOD: <paramref name="Reps"/> are per round.</param>
public sealed record CatalogTemplateExercise(string ExerciseKey, int Sets, int Reps, int Seconds = 0, bool LinkedToNext = false, bool IsWod = false);

/// <summary>The WOD that ends a CrossFit template, its movements being the template exercises marked IsWod.</summary>
public sealed record CatalogWod(WodFormat Format, int Minutes);

/// <param name="Key">Also the translation key of its name ("Template_" + key).</param>
public sealed record CatalogTemplate(string Key, IReadOnlyList<CatalogTemplateExercise> Exercises, CatalogWod? Wod = null);

/// <param name="Key">Also the translation key of its name ("Program_" + key) and description ("ProgramText_" + key).</param>
/// <param name="SessionsPerWeek">Workouts per week it is designed for.</param>
public sealed record CatalogProgram(string Key, int SessionsPerWeek, int Weeks, IReadOnlyList<CatalogTemplate> Templates,
    TrainingSport Sport = TrainingSport.Strength);

/// <summary>Ready-made programs, each a few workout templates done in turn.</summary>
public static class ProgramCatalog
{
    public static readonly IReadOnlyList<CatalogProgram> All =
    [
        new("full_body", 3, 8,
        [
            new("full_body_a",
            [
                new("squat", 3, 8),
                new("bench_press", 3, 8),
                new("barbell_row", 3, 8),
                new("lateral_raise", 3, 12),
                new("plank", 3, 0, Seconds: 45),
            ]),
            new("full_body_b",
            [
                new("deadlift", 3, 5),
                new("overhead_press", 3, 8),
                new("lat_pulldown", 3, 10),
                new("bulgarian_split_squat", 3, 10),
                new("hanging_leg_raise", 3, 10),
            ]),
        ]),
        new("five_by_five", 3, 12,
        [
            new("five_by_five_a",
            [
                new("squat", 5, 5),
                new("bench_press", 5, 5),
                new("barbell_row", 5, 5),
            ]),
            new("five_by_five_b",
            [
                new("squat", 5, 5),
                new("overhead_press", 5, 5),
                new("deadlift", 1, 5),
            ]),
        ]),
        new("upper_lower", 4, 8,
        [
            new("upper_a",
            [
                new("bench_press", 4, 6),
                new("barbell_row", 4, 6),
                new("overhead_press", 3, 8),
                new("lat_pulldown", 3, 10),
                new("barbell_curl", 3, 10, LinkedToNext: true),
                new("triceps_pushdown", 3, 12),
            ]),
            new("lower_a",
            [
                new("squat", 4, 6),
                new("romanian_deadlift", 3, 8),
                new("leg_press", 3, 10),
                new("leg_curl", 3, 12),
                new("calf_raise", 4, 12),
            ]),
            new("upper_b",
            [
                new("incline_dumbbell_press", 4, 8),
                new("pull_up", 4, 8),
                new("dumbbell_shoulder_press", 3, 10),
                new("seated_cable_row", 3, 10),
                new("lateral_raise", 3, 15, LinkedToNext: true),
                new("face_pull", 3, 15),
            ]),
            new("lower_b",
            [
                new("deadlift", 3, 5),
                new("front_squat", 3, 8),
                new("bulgarian_split_squat", 3, 10),
                new("leg_extension", 3, 12),
                new("hanging_leg_raise", 3, 12),
            ]),
        ]),
        new("push_pull_legs", 6, 8,
        [
            new("push",
            [
                new("bench_press", 4, 8),
                new("incline_dumbbell_press", 3, 10),
                new("overhead_press", 3, 8),
                new("lateral_raise", 3, 15),
                new("triceps_pushdown", 3, 12, LinkedToNext: true),
                new("overhead_triceps_extension", 3, 12),
            ]),
            new("pull",
            [
                new("deadlift", 3, 5),
                new("pull_up", 4, 8),
                new("barbell_row", 3, 10),
                new("face_pull", 3, 15),
                new("hammer_curl", 3, 12),
            ]),
            new("legs",
            [
                new("squat", 4, 8),
                new("romanian_deadlift", 3, 10),
                new("leg_press", 3, 12),
                new("leg_curl", 3, 12),
                new("calf_raise", 4, 15),
                new("plank", 3, 0, Seconds: 60),
            ]),
        ]),
        new("crossfit_foundations", 3, 8,
        [
            new("cf_foundations_a",
            [
                new("squat", 5, 5),
                new("wall_ball", 1, 10, LinkedToNext: true, IsWod: true),
                new("kettlebell_swing", 1, 12, LinkedToNext: true, IsWod: true),
                new("box_jump", 1, 10, IsWod: true),
            ], new CatalogWod(WodFormat.Amrap, 12)),
            new("cf_foundations_b",
            [
                new("push_press", 5, 5),
                new("thruster", 3, 12, LinkedToNext: true, IsWod: true),
                new("pull_up", 3, 10, IsWod: true),
            ], new CatalogWod(WodFormat.ForTime, 15)),
            new("cf_foundations_c",
            [
                new("deadlift", 5, 3),
                new("kettlebell_swing", 1, 12, LinkedToNext: true, IsWod: true),
                new("burpee", 1, 8, LinkedToNext: true, IsWod: true),
                new("air_squat", 1, 15, IsWod: true),
            ], new CatalogWod(WodFormat.Emom, 12)),
        ], TrainingSport.CrossFit),
        new("crossfit_performance", 5, 8,
        [
            new("cf_performance_a",
            [
                new("power_clean", 5, 3),
                new("front_squat", 4, 5),
                new("thruster", 3, 15, LinkedToNext: true, IsWod: true),
                new("pull_up", 3, 15, IsWod: true),
            ], new CatalogWod(WodFormat.ForTime, 12)),
            new("cf_performance_b",
            [
                new("snatch", 5, 2),
                new("double_under", 1, 30, LinkedToNext: true, IsWod: true),
                new("toes_to_bar", 1, 10, LinkedToNext: true, IsWod: true),
                new("kettlebell_swing", 1, 15, IsWod: true),
            ], new CatalogWod(WodFormat.Amrap, 15)),
            new("cf_performance_c",
            [
                new("squat", 5, 5),
                new("assault_bike", 1, 0, Seconds: 40, LinkedToNext: true, IsWod: true),
                new("burpee", 1, 10, LinkedToNext: true, IsWod: true),
                new("wall_ball", 1, 15, LinkedToNext: true, IsWod: true),
                new("toes_to_bar", 1, 12, IsWod: true),
            ], new CatalogWod(WodFormat.Emom, 16)),
            new("cf_performance_d",
            [
                new("clean_and_jerk", 6, 2),
                new("muscle_up", 5, 3, LinkedToNext: true, IsWod: true),
                new("box_jump", 5, 10, LinkedToNext: true, IsWod: true),
                new("deadlift", 5, 8, IsWod: true),
            ], new CatalogWod(WodFormat.ForTime, 20)),
            new("cf_performance_e",
            [
                new("push_press", 5, 3),
                new("handstand_push_up", 1, 5, LinkedToNext: true, IsWod: true),
                new("pull_up", 1, 10, LinkedToNext: true, IsWod: true),
                new("air_squat", 1, 15, IsWod: true),
            ], new CatalogWod(WodFormat.Amrap, 20)),
        ], TrainingSport.CrossFit),
    ];

    /// <summary>The program that fits a number of workouts per week best.</summary>
    public static CatalogProgram Suggest(int sessionsPerWeek) => sessionsPerWeek switch
    {
        <= 3 => Find("full_body"),
        4 => Find("upper_lower"),
        _ => Find("push_pull_legs"),
    };

    /// <summary>The program for a sport, a goal and a number of workouts per week.</summary>
    public static CatalogProgram Suggest(TrainingSport sport, TrainingGoal goal, int sessionsPerWeek) =>
        sport == TrainingSport.CrossFit
            ? Find(sessionsPerWeek >= 5 ? "crossfit_performance" : "crossfit_foundations")
            : Suggest(goal, sessionsPerWeek);

    /// <summary>The program for a goal and a number of workouts per week, its repetitions adapted to the goal.</summary>
    public static CatalogProgram Suggest(TrainingGoal goal, int sessionsPerWeek) =>
        Adapt(goal == TrainingGoal.Strength && sessionsPerWeek <= 3 ? Find("five_by_five") : Suggest(sessionsPerWeek), goal);

    /// <summary>
    /// Repetitions suited to the goal: heavy compound lifts stay at 6 or fewer for strength, every set reaches 12
    /// for fitness. The 5×5 program is left as it is, it is a strength program already.
    /// </summary>
    public static CatalogProgram Adapt(CatalogProgram program, TrainingGoal goal)
    {
        if (goal == TrainingGoal.Muscle || program.Key == "five_by_five" || program.Sport == TrainingSport.CrossFit)
            return program;
        return program with
        {
            Templates = program.Templates
                .Select(t => t with { Exercises = t.Exercises.Select(e => e.Reps == 0 ? e : e with { Reps = AdaptReps(e.Reps, goal) }).ToList() })
                .ToList(),
        };
    }

    /// <summary>Rest between two sets that suits the goal, in seconds.</summary>
    public static int RestSeconds(TrainingGoal goal) => goal switch
    {
        TrainingGoal.Strength => 180,
        TrainingGoal.Fitness => 60,
        _ => 90,
    };

    /// <summary>Repetitions offered for an exercise added to a template.</summary>
    public static int DefaultReps(TrainingGoal goal) => goal switch
    {
        TrainingGoal.Strength => 5,
        TrainingGoal.Fitness => 12,
        _ => 10,
    };

    public static CatalogProgram Find(string key) => All.Single(p => p.Key == key);

    private static int AdaptReps(int reps, TrainingGoal goal) => goal switch
    {
        TrainingGoal.Strength when reps <= 8 => Math.Min(reps, 5),
        TrainingGoal.Fitness => Math.Max(reps, 12),
        _ => reps,
    };
}
