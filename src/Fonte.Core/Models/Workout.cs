using System.Text.Json.Serialization;
using SQLite;

namespace Fonte.Core.Models;

/// <summary>What a set is for. Only normal and to-failure sets count towards records, volume and targets.</summary>
public enum SetKind
{
    Normal = 0,

    /// <summary>Light set before the working sets.</summary>
    WarmUp = 1,

    /// <summary>Lighter set right after a working set, without rest.</summary>
    Drop = 2,

    /// <summary>Taken until no more repetition is possible.</summary>
    Failure = 3,
}

/// <summary>A training session. It stays "in progress" until it is finished.</summary>
/// <summary>How a CrossFit WOD is scored.</summary>
public enum WodFormat
{
    None = 0,

    /// <summary>As many rounds as possible in a set time: scored in rounds and repetitions.</summary>
    Amrap = 1,

    /// <summary>Every minute on the minute: the work starts at each new minute, for a set number of minutes.</summary>
    Emom = 2,

    /// <summary>The work done as fast as possible, within a time cap: scored in time.</summary>
    ForTime = 3,
}

[Table("workouts")]
public sealed class Workout
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>How the session felt, from 1 (hard) to 5 (great); null when not given.</summary>
    public int? Feeling { get; set; }

    /// <summary>Template it was started from; null for a free workout.</summary>
    public int? TemplateId { get; set; }

    /// <summary>The WOD of the workout (CrossFit), copied from its template or chosen on the timer.</summary>
    public WodFormat WodFormat { get; set; }

    /// <summary>Its duration (AMRAP, EMOM) or time cap (for time), in minutes.</summary>
    public int WodMinutes { get; set; }

    /// <summary>Score: complete rounds (AMRAP), and the repetitions of the round not finished.</summary>
    public int WodRounds { get; set; }

    public int WodReps { get; set; }

    /// <summary>Score of a "for time" WOD, in seconds; 0 when the time cap was reached.</summary>
    public int WodSeconds { get; set; }

    [Ignore, JsonIgnore]
    public bool HasWodScore => WodFormat != WodFormat.None && (WodRounds > 0 || WodReps > 0 || WodSeconds > 0);

    [Ignore, JsonIgnore]
    public bool IsInProgress => FinishedAt is null;

    [Ignore, JsonIgnore]
    public TimeSpan Duration => (FinishedAt ?? DateTime.Now) - StartedAt;
}

/// <summary>An exercise done during a workout, in the order they were added.</summary>
[Table("workout_exercises")]
public sealed class WorkoutExercise
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int WorkoutId { get; set; }

    [Indexed]
    public int ExerciseId { get; set; }

    public int Position { get; set; }

    /// <summary>Done back to back with the next exercise, without resting in between (superset).</summary>
    public bool LinkedToNext { get; set; }

    /// <summary>What the template aimed for, shown during the workout; null for an exercise added freely.</summary>
    public int? TargetSets { get; set; }

    public int? TargetReps { get; set; }

    public double? TargetWeight { get; set; }

    public int? TargetSeconds { get; set; }

    /// <summary>Rest after each set of this exercise; null for the rest chosen in the settings.</summary>
    public int? RestSeconds { get; set; }

    /// <summary>A movement of the WOD: no rest timer between its sets, no automatic progression.</summary>
    public bool IsWod { get; set; }
}

[Table("workout_sets")]
public sealed class WorkoutSet
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int WorkoutExerciseId { get; set; }

    public int Position { get; set; }

    /// <summary>Load in kilograms (<see cref="Tracking.WeightAndReps"/>).</summary>
    public double Weight { get; set; }

    public int Reps { get; set; }

    /// <summary>Duration (<see cref="Tracking.Time"/>).</summary>
    public int Seconds { get; set; }

    public SetKind Kind { get; set; }

    /// <summary>Rate of perceived exertion, from 1 (very easy) to 10 (nothing left).</summary>
    public double? Rpe { get; set; }

    /// <summary>Ticked once performed; sets left unticked are dropped when the workout is finished.</summary>
    public bool IsDone { get; set; }

    public DateTime? DoneAt { get; set; }
}
