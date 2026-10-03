using SQLite;

namespace Fonte.Core.Models;

[Table("body_weights")]
public sealed class BodyWeight
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>Day of the weighing (one per day).</summary>
    [Indexed]
    public DateTime Date { get; set; }

    public double Kilograms { get; set; }
}

public enum MeasurementKind
{
    Waist = 0,
    Chest = 1,
    Arms = 2,
    Thighs = 3,
    Hips = 4,
}

[Table("measurements")]
public sealed class BodyMeasurement
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public DateTime Date { get; set; }

    public MeasurementKind Kind { get; set; }

    public double Centimetres { get; set; }
}

/// <summary>A progress photo. The picture itself is a file in the app's private storage.</summary>
[Table("body_photos")]
public sealed class BodyPhoto
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public DateTime Date { get; set; }

    [MaxLength(80)]
    public string FileName { get; set; } = string.Empty;
}
