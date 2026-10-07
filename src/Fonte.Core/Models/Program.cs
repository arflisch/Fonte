using SQLite;

namespace Fonte.Core.Models;

/// <summary>A training plan: workout templates done in turn, over a number of weeks.</summary>
[Table("programs")]
public sealed class TrainingProgram
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The ready-made program it was created from, if any.</summary>
    public string? CatalogKey { get; set; }

    /// <summary>Planned length; 0 when open-ended.</summary>
    public int Weeks { get; set; }

    /// <summary>Only one program is followed at a time: its next workout is offered on the home screen.</summary>
    public bool IsActive { get; set; }

    /// <summary>When it was last made active: week 1 starts then.</summary>
    public DateTime StartedOn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>A planned workout ("Push", "Full body A"): exercises with their sets, repetitions and loads to aim for.</summary>
[Table("templates")]
public sealed class WorkoutTemplate
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Program it belongs to; null for a template of its own.</summary>
    [Indexed]
    public int? ProgramId { get; set; }

    [MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Order within the program: templates are done in turn.</summary>
    public int Position { get; set; }

    /// <summary>The ready-made workout it was created from; null for one the user created.</summary>
    public string? CatalogKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

[Table("template_exercises")]
public sealed class TemplateExercise
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int TemplateId { get; set; }

    [Indexed]
    public int ExerciseId { get; set; }

    public int Position { get; set; }

    public int Sets { get; set; }

    /// <summary>Repetitions to reach on each set.</summary>
    public int Reps { get; set; }

    /// <summary>Load to use, in kilograms; 0 until it is known (taken from the first workout).</summary>
    public double Weight { get; set; }

    /// <summary>Duration to hold, for timed exercises.</summary>
    public int Seconds { get; set; }

    /// <summary>Done back to back with the next exercise, without resting in between (superset).</summary>
    public bool LinkedToNext { get; set; }

    /// <summary>Rest after each set, in seconds; 0 for the rest chosen in the settings.</summary>
    public int RestSeconds { get; set; }
}
