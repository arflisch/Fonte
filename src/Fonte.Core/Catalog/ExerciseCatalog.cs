using Fonte.Core.Models;

namespace Fonte.Core.Catalog;

/// <param name="Key">Stable identifier, also the translation key of the name ("Ex_" + key).</param>
public sealed record CatalogExercise(string Key, MuscleGroup Muscle, Equipment Equipment, Tracking Tracking = Tracking.WeightAndReps);

/// <summary>The exercises every user starts with. Keys never change: past workouts refer to them.</summary>
public static class ExerciseCatalog
{
    public static readonly IReadOnlyList<CatalogExercise> All =
    [
        // Chest
        new("bench_press", MuscleGroup.Chest, Equipment.Barbell),
        new("incline_bench_press", MuscleGroup.Chest, Equipment.Barbell),
        new("dumbbell_bench_press", MuscleGroup.Chest, Equipment.Dumbbell),
        new("incline_dumbbell_press", MuscleGroup.Chest, Equipment.Dumbbell),
        new("dumbbell_fly", MuscleGroup.Chest, Equipment.Dumbbell),
        new("cable_crossover", MuscleGroup.Chest, Equipment.Cable),
        new("chest_press_machine", MuscleGroup.Chest, Equipment.Machine),
        new("push_up", MuscleGroup.Chest, Equipment.Bodyweight, Tracking.Reps),
        new("dips", MuscleGroup.Chest, Equipment.Bodyweight, Tracking.Reps),

        // Back
        new("deadlift", MuscleGroup.Back, Equipment.Barbell),
        new("barbell_row", MuscleGroup.Back, Equipment.Barbell),
        new("dumbbell_row", MuscleGroup.Back, Equipment.Dumbbell),
        new("pull_up", MuscleGroup.Back, Equipment.Bodyweight, Tracking.Reps),
        new("chin_up", MuscleGroup.Back, Equipment.Bodyweight, Tracking.Reps),
        new("lat_pulldown", MuscleGroup.Back, Equipment.Cable),
        new("seated_cable_row", MuscleGroup.Back, Equipment.Cable),
        new("back_extension", MuscleGroup.Back, Equipment.Bodyweight, Tracking.Reps),

        // Shoulders
        new("overhead_press", MuscleGroup.Shoulders, Equipment.Barbell),
        new("dumbbell_shoulder_press", MuscleGroup.Shoulders, Equipment.Dumbbell),
        new("lateral_raise", MuscleGroup.Shoulders, Equipment.Dumbbell),
        new("front_raise", MuscleGroup.Shoulders, Equipment.Dumbbell),
        new("rear_delt_fly", MuscleGroup.Shoulders, Equipment.Dumbbell),
        new("face_pull", MuscleGroup.Shoulders, Equipment.Cable),
        new("shrug", MuscleGroup.Shoulders, Equipment.Dumbbell),

        // Arms
        new("barbell_curl", MuscleGroup.Arms, Equipment.Barbell),
        new("dumbbell_curl", MuscleGroup.Arms, Equipment.Dumbbell),
        new("hammer_curl", MuscleGroup.Arms, Equipment.Dumbbell),
        new("triceps_pushdown", MuscleGroup.Arms, Equipment.Cable),
        new("skull_crusher", MuscleGroup.Arms, Equipment.Barbell),
        new("overhead_triceps_extension", MuscleGroup.Arms, Equipment.Dumbbell),
        new("close_grip_bench_press", MuscleGroup.Arms, Equipment.Barbell),

        // Legs
        new("squat", MuscleGroup.Legs, Equipment.Barbell),
        new("front_squat", MuscleGroup.Legs, Equipment.Barbell),
        new("leg_press", MuscleGroup.Legs, Equipment.Machine),
        new("romanian_deadlift", MuscleGroup.Legs, Equipment.Barbell),
        new("lunge", MuscleGroup.Legs, Equipment.Dumbbell),
        new("bulgarian_split_squat", MuscleGroup.Legs, Equipment.Dumbbell),
        new("leg_extension", MuscleGroup.Legs, Equipment.Machine),
        new("leg_curl", MuscleGroup.Legs, Equipment.Machine),
        new("hip_thrust", MuscleGroup.Legs, Equipment.Barbell),
        new("calf_raise", MuscleGroup.Legs, Equipment.Machine),
        new("goblet_squat", MuscleGroup.Legs, Equipment.Kettlebell),

        // Core
        new("plank", MuscleGroup.Core, Equipment.Bodyweight, Tracking.Time),
        new("crunch", MuscleGroup.Core, Equipment.Bodyweight, Tracking.Reps),
        new("hanging_leg_raise", MuscleGroup.Core, Equipment.Bodyweight, Tracking.Reps),
        new("cable_crunch", MuscleGroup.Core, Equipment.Cable),
        new("ab_wheel", MuscleGroup.Core, Equipment.Other, Tracking.Reps),

        // Cardio
        new("running", MuscleGroup.Cardio, Equipment.Other, Tracking.Time),
        new("rowing_machine", MuscleGroup.Cardio, Equipment.Machine, Tracking.Time),
        new("cycling", MuscleGroup.Cardio, Equipment.Machine, Tracking.Time),
        new("jump_rope", MuscleGroup.Cardio, Equipment.Other, Tracking.Time),
    ];
}
