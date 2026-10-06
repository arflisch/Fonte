using System.Formats.Tar;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fonte.Core.Catalog;
using Fonte.Core.Models;

namespace Fonte.Core.Backup;

/// <summary>The app's preferences that travel with a backup. The language stays the one of the new device.</summary>
public sealed record BackupSettings(
    bool RestTimerEnabled,
    int RestSeconds,
    TrainingGoal Goal,
    int SessionsPerWeek,
    double GoalWeight,
    double BarWeight,
    IReadOnlyList<double> Plates,
    string Accent);

/// <summary>Every row of the database. <see cref="Photos"/> is null when the backup was made without the photos.</summary>
public sealed class BackupData
{
    public List<Exercise> Exercises { get; set; } = [];

    public List<Workout> Workouts { get; set; } = [];

    public List<WorkoutExercise> WorkoutExercises { get; set; } = [];

    public List<WorkoutSet> WorkoutSets { get; set; } = [];

    public List<TrainingProgram> Programs { get; set; } = [];

    public List<WorkoutTemplate> Templates { get; set; } = [];

    public List<TemplateExercise> TemplateExercises { get; set; } = [];

    public List<BodyWeight> BodyWeights { get; set; } = [];

    public List<BodyMeasurement> Measurements { get; set; } = [];

    public List<BodyPhoto>? Photos { get; set; }
}

public sealed record BackupContent(DateTimeOffset ExportedAt, BackupData Data, BackupSettings Settings)
{
    public bool IncludesPhotos => Data.Photos is not null;
}

/// <summary>
/// A backup file: a tar archive with <c>data.json</c> and, when chosen, the pictures under <c>photos/</c>, encrypted
/// as a stream by <see cref="BackupCrypto"/>. Photos are copied from file to file, never held in memory.
/// </summary>
public static class BackupFile
{
    public const int MinPasswordLength = 8;
    public const string Extension = ".fontebackup";

    private const string DataEntry = "data.json";
    private const string PhotoPrefix = "photos/";

    /// <summary>Years of training weigh a few megabytes: anything far larger is not a backup of ours.</summary>
    private const long MaxDataLength = 256L * 1024 * 1024;

    /// <summary>
    /// Writes an encrypted backup to <paramref name="output"/>. When the content has photos, each picture is read
    /// from <paramref name="photoFolder"/>; a photo whose picture is gone is left out.
    /// </summary>
    public static async Task WriteAsync(
        Stream output, BackupContent content, string photoFolder, string password, CancellationToken cancellationToken)
    {
        var photos = content.Data.Photos?
            .Where(p => IsPhotoName(p.FileName) && File.Exists(Path.Combine(photoFolder, p.FileName)))
            .ToList();
        var document = new BackupDocument
        {
            ExportedAt = content.ExportedAt,
            Settings = content.Settings,
            Data = With(content.Data, photos),
        };

        await using var encrypted = await BackupCrypto.StartEncryptingAsync(output, password, cancellationToken);
        await using (var archive = new TarWriter(encrypted, TarEntryFormat.Ustar, leaveOpen: true))
        {
            using var json = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document, BackupJsonContext.Default.BackupDocument));
            await archive.WriteEntryAsync(Entry(DataEntry, json, content.ExportedAt), cancellationToken);

            foreach (var photo in photos ?? [])
            {
                await using var picture = File.OpenRead(Path.Combine(photoFolder, photo.FileName));
                await archive.WriteEntryAsync(Entry(PhotoPrefix + photo.FileName, picture, content.ExportedAt), cancellationToken);
            }
        }
        await encrypted.FinishAsync(cancellationToken);
    }

    /// <summary>Checks the start of a file, before asking for its password.</summary>
    /// <exception cref="FonteException">Not a Fonte backup, or one from a newer version.</exception>
    public static void CheckHeader(Stream input)
    {
        Span<byte> header = stackalloc byte[BackupCrypto.HeaderSize];
        var read = input.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        BackupCrypto.CheckHeader(header[..read]);
    }

    /// <summary>
    /// Decrypts and checks the whole backup. Its pictures are written to <paramref name="photoFolder"/>, a folder of
    /// their own until the user confirms the restore.
    /// </summary>
    /// <exception cref="FonteException">Not a backup, newer version, wrong password, or a damaged file.</exception>
    public static async Task<BackupContent> ReadAsync(
        Stream input, string password, string photoFolder, CancellationToken cancellationToken)
    {
        await using var decrypted = await BackupCrypto.StartDecryptingAsync(input, password, cancellationToken);
        BackupDocument? document = null;
        var pictures = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            await using (var archive = new TarReader(decrypted, leaveOpen: true))
            {
                while (await archive.GetNextEntryAsync(copyData: false, cancellationToken) is { } entry)
                {
                    if (entry.DataStream is null || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                        continue;

                    if (entry.Name == DataEntry && entry.Length <= MaxDataLength)
                    {
                        document = await JsonSerializer.DeserializeAsync(entry.DataStream, BackupJsonContext.Default.BackupDocument, cancellationToken);
                    }
                    else if (entry.Name.StartsWith(PhotoPrefix, StringComparison.Ordinal) && entry.Name[PhotoPrefix.Length..] is var name && IsPhotoName(name))
                    {
                        Directory.CreateDirectory(photoFolder);
                        await using var picture = File.Create(Path.Combine(photoFolder, name));
                        await entry.DataStream.CopyToAsync(picture, cancellationToken);
                        pictures.Add(name);
                    }
                }
            }

            // The archive can end before the file does: read to the end, so a cut-off file is noticed.
            await decrypted.CopyToAsync(Stream.Null, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FormatException)
        {
            throw new FonteException(FonteError.BackupDamaged, $"The backup's content is unreadable: {ex.Message}");
        }

        if (document is null || document.Format != BackupDocument.FormatName || document.Data is null || document.Settings is null)
            throw new FonteException(FonteError.BackupDamaged, "The backup has no data.");
        if (document.Version > BackupDocument.CurrentVersion)
            throw new FonteException(FonteError.BackupFromNewerVersion, $"Backup data version {document.Version} is not supported.");

        // Only photos whose picture came with them: a photo without a picture could not be shown.
        var photos = document.Data.Photos?.Where(p => pictures.Contains(p.FileName)).ToList();
        return new BackupContent(document.ExportedAt, With(document.Data, photos), document.Settings);
    }

    /// <summary>Names the app gives pictures (a GUID and an extension): anything else could point outside the folder.</summary>
    private static bool IsPhotoName(string name) =>
        name.Length is > 0 and <= 80
        && name[0] != '.'
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static BackupData With(BackupData data, List<BodyPhoto>? photos) => new()
    {
        Exercises = data.Exercises,
        Workouts = data.Workouts,
        WorkoutExercises = data.WorkoutExercises,
        WorkoutSets = data.WorkoutSets,
        Programs = data.Programs,
        Templates = data.Templates,
        TemplateExercises = data.TemplateExercises,
        BodyWeights = data.BodyWeights,
        Measurements = data.Measurements,
        Photos = photos,
    };

    private static UstarTarEntry Entry(string name, Stream content, DateTimeOffset time) =>
        new(TarEntryType.RegularFile, name) { DataStream = content, ModificationTime = time };
}

/// <summary>The <c>data.json</c> of a backup.</summary>
internal sealed class BackupDocument
{
    public const string FormatName = "fonte-backup";

    /// <summary>Raised only for a change older versions would misread; new fields are simply ignored by them.</summary>
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatName;

    public int Version { get; set; } = CurrentVersion;

    public DateTimeOffset ExportedAt { get; set; }

    public BackupSettings? Settings { get; set; }

    public BackupData? Data { get; set; }
}

/// <summary>
/// Dates of workouts and weighings are the phone's wall-clock time: kept as such, without a time zone, so a workout
/// done at 18:00 still reads 18:00 on a phone set to another time zone.
/// </summary>
internal sealed class WallClockDateTimeConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.FFFFFFF";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTime.SpecifyKind(DateTime.ParseExact(reader.GetString()!, Format, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, Converters = [typeof(WallClockDateTimeConverter)])]
[JsonSerializable(typeof(BackupDocument))]
internal sealed partial class BackupJsonContext : JsonSerializerContext;
