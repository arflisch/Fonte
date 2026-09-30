using SQLite;

namespace Fonte.Core.Models;

public enum MuscleGroup
{
    Chest = 0,
    Back = 1,
    Shoulders = 2,
    Arms = 3,
    Legs = 4,
    Core = 5,
    Cardio = 6,
}

public enum Equipment
{
    Barbell = 0,
    Dumbbell = 1,
    Machine = 2,
    Cable = 3,
    Bodyweight = 4,
    Kettlebell = 5,
    Other = 6,
}

/// <summary>What each set of an exercise records.</summary>
public enum Tracking
{
    /// <summary>A load and a number of repetitions: bench press, squat…</summary>
    WeightAndReps = 0,

    /// <summary>Repetitions only: push-ups, pull-ups…</summary>
    Reps = 1,

    /// <summary>A duration in seconds: plank, rowing machine…</summary>
    Time = 2,
}

[Table("exercises")]
public sealed class Exercise
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Built-in exercises: their key in the catalog (the app translates the name). Null for the user's own.</summary>
    [Indexed]
    public string? CatalogKey { get; set; }

    /// <summary>Name of an exercise created by the user.</summary>
    [MaxLength(60)]
    public string? CustomName { get; set; }

    public MuscleGroup Muscle { get; set; }

    public Equipment Equipment { get; set; }

    public Tracking Tracking { get; set; }

    /// <summary>Removed from the library by the user, but kept so past workouts still show it.</summary>
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Ignore]
    public bool IsCustom => CatalogKey is null;
}
