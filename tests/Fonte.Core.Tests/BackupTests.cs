using System.Text;
using Fonte.Core.Backup;
using Fonte.Core.Catalog;
using Fonte.Core.Data;
using Fonte.Core.Models;

namespace Fonte.Core.Tests;

public sealed class BackupTests : StoreTestBase, IDisposable
{
    private const string Password = "correct horse battery";

    private static readonly DateTimeOffset ExportedAt = new(2026, 10, 6, 20, 0, 0, TimeSpan.FromHours(2));

    private static readonly BackupSettings Settings =
        new(false, 120, TrainingGoal.Strength, 4, 78.5, 15, [20, 10, 5, 1.25], "green");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fonte-backup-{Guid.NewGuid():N}");

    private string PhotoFolder => Path.Combine(_folder, "photos");

    private string StagingFolder => Path.Combine(_folder, "staging");

    [Fact]
    public async Task A_backup_restores_everything_on_another_phone()
    {
        var bench = await IdOf("bench_press");
        var custom = await Store.SaveCustomExerciseAsync(new Exercise { CustomName = "Landmine press", Muscle = MuscleGroup.Shoulders });
        var first = await DoWorkoutAsync(Monday, (bench, [(80, 6), (82.5, 5)]), (custom.Id, [(30, 10)]));
        await DoWorkoutAsync(Monday.AddDays(2), (bench, [(85, 5)]));
        var program = await Store.CreateProgramFromCatalogAsync(ProgramCatalog.Suggest(3), key => key, Monday);
        await Store.ActivateProgramAsync(program.Id, Monday);
        await Store.SaveWeightAsync(Monday, 81.4);
        await Store.SaveMeasurementAsync(Monday, MeasurementKind.Waist, 84);
        var photo = await AddPhotoAsync(Monday, 300_000);

        var file = await WriteAsync(await Store.ExportAsync(includePhotos: true));
        var restored = await ReadAsync(file);

        await using var newPhone = new NewPhone();
        await newPhone.Store.RestoreAsync(restored.Data);

        Assert.Equal(ExportedAt, restored.ExportedAt);
        Assert.Equal(Settings with { Plates = restored.Settings.Plates }, restored.Settings);
        Assert.Equal(Settings.Plates, restored.Settings.Plates);
        var summary = (await newPhone.Store.GetWorkoutSummaryAsync(first.Workout.Id))!;
        Assert.Equal(first.Workout.StartedAt, summary.Workout.StartedAt);
        Assert.Equal(first.Volume, summary.Volume);
        Assert.Equal(2, (await newPhone.Store.GetRecentWorkoutsAsync(10)).Count);
        Assert.Contains(await newPhone.Store.GetExercisesAsync(), e => e.Id == custom.Id && e.CustomName == "Landmine press");
        Assert.Equal(program.Id, (await newPhone.Store.GetActiveProgramAsync(Monday))!.Program.Id);
        Assert.Equal(81.4, Assert.Single(await newPhone.Store.GetWeightsAsync()).Kilograms);
        Assert.Equal(84, Assert.Single(await newPhone.Store.GetMeasurementsAsync()).Centimetres);
        Assert.Equal(photo.FileName, Assert.Single(await newPhone.Store.GetPhotosAsync()).FileName);
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(PhotoFolder, photo.FileName)),
            await File.ReadAllBytesAsync(Path.Combine(StagingFolder, photo.FileName)));
    }

    [Fact]
    public async Task Dates_keep_the_time_of_day_they_were_recorded_at()
    {
        await DoWorkoutAsync(Monday, (await IdOf("squat"), [(100, 5)]));

        var restored = await ReadAsync(await WriteAsync(await Store.ExportAsync(includePhotos: false)));

        var workout = Assert.Single(restored.Data.Workouts);
        Assert.Equal(Monday, workout.StartedAt);
        Assert.Equal(DateTimeKind.Unspecified, workout.StartedAt.Kind);
    }

    [Fact]
    public async Task A_backup_without_photos_keeps_the_photos_already_there()
    {
        await AddPhotoAsync(Monday, 1000);
        var backup = await ReadAsync(await WriteAsync(await Store.ExportAsync(includePhotos: false)));

        await using var newPhone = new NewPhone();
        await newPhone.Store.AddPhotoAsync(Monday, "kept.jpg");
        await newPhone.Store.RestoreAsync(backup.Data);

        Assert.False(backup.IncludesPhotos);
        Assert.False(Directory.Exists(StagingFolder));
        Assert.Equal("kept.jpg", Assert.Single(await newPhone.Store.GetPhotosAsync()).FileName);
    }

    [Fact]
    public async Task Restoring_replaces_what_was_there()
    {
        await using var newPhone = new NewPhone();
        await newPhone.Store.SaveWeightAsync(Monday, 90);
        await newPhone.Store.AddPhotoAsync(Monday, "old.jpg");
        var photo = await AddPhotoAsync(Monday.AddDays(1), 1000);

        await newPhone.Store.RestoreAsync((await ReadAsync(await WriteAsync(await Store.ExportAsync(includePhotos: true)))).Data);

        Assert.Empty(await newPhone.Store.GetWeightsAsync());
        Assert.Equal(photo.FileName, Assert.Single(await newPhone.Store.GetPhotosAsync()).FileName);
    }

    [Fact]
    public async Task Exercises_added_to_the_catalog_since_the_backup_are_still_there()
    {
        var data = await Store.ExportAsync(includePhotos: false);
        data.Exercises.RemoveAll(e => e.CatalogKey == "squat");

        await using var newPhone = new NewPhone();
        await newPhone.Store.RestoreAsync(data);

        Assert.Contains(await newPhone.Store.GetExercisesAsync(), e => e.CatalogKey == "squat");
    }

    [Fact]
    public async Task A_photo_whose_picture_is_gone_is_left_out()
    {
        var kept = await AddPhotoAsync(Monday, 1000);
        await Store.AddPhotoAsync(Monday, "missing.jpg");

        var restored = await ReadAsync(await WriteAsync(await Store.ExportAsync(includePhotos: true)));

        Assert.Equal(kept.FileName, Assert.Single(restored.Data.Photos!).FileName);
    }

    [Fact]
    public async Task The_file_reveals_nothing_without_the_password()
    {
        await Store.SaveCustomExerciseAsync(new Exercise { CustomName = "Secret squat" });
        var photo = await AddPhotoAsync(Monday, 1000);

        var file = await WriteAsync(await Store.ExportAsync(includePhotos: true));
        var text = Encoding.Latin1.GetString(file);

        Assert.DoesNotContain("Secret squat", text, StringComparison.Ordinal);
        Assert.DoesNotContain("data.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain(photo.FileName, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_password_is_refused()
    {
        var file = await WriteAsync(await Store.ExportAsync(includePhotos: false));

        await AssertFailsAsync(FonteError.BackupWrongPassword, file, "wrong password");
    }

    [Fact]
    public async Task A_change_at_the_start_reads_as_a_wrong_password_and_later_as_damage()
    {
        await AddPhotoAsync(Monday, 300_000);
        var file = await WriteAsync(await Store.ExportAsync(includePhotos: true));

        await AssertFailsAsync(FonteError.BackupWrongPassword, Flip(file, BackupCrypto.HeaderSize + 10));
        await AssertFailsAsync(FonteError.BackupDamaged, Flip(file, file.Length - 100));
    }

    [Fact]
    public async Task A_cut_off_file_is_refused()
    {
        await AddPhotoAsync(Monday, 300_000);
        var file = await WriteAsync(await Store.ExportAsync(includePhotos: true));
        const int chunk = 64 * 1024 + BackupCrypto.TagSize;

        await AssertFailsAsync(FonteError.BackupDamaged, file[..(BackupCrypto.HeaderSize + 2 * chunk)]);
        await AssertFailsAsync(FonteError.BackupDamaged, file[..^1]);
    }

    [Fact]
    public async Task The_header_is_checked_before_asking_for_the_password()
    {
        var file = await WriteAsync(await Store.ExportAsync(includePhotos: false));
        var newer = (byte[])file.Clone();
        newer[8] = BackupCrypto.CurrentVersion + 1;

        BackupFile.CheckHeader(new MemoryStream(file));
        Assert.Equal(FonteError.BackupFromNewerVersion,
            Assert.Throws<FonteException>(() => BackupFile.CheckHeader(new MemoryStream(newer))).Error);
        foreach (var other in new[] { "{\"format\":\"poches-backup-encrypted\"}"u8.ToArray(), [], new byte[100] })
        {
            Assert.Equal(FonteError.BackupInvalidFile,
                Assert.Throws<FonteException>(() => BackupFile.CheckHeader(new MemoryStream(other))).Error);
            await AssertFailsAsync(FonteError.BackupInvalidFile, other);
        }
    }

    [Fact]
    public async Task An_accented_password_gives_the_same_key_whatever_its_unicode_form()
    {
        var composed = "café-crème".Normalize(NormalizationForm.FormC);
        var file = await WriteAsync(await Store.ExportAsync(includePhotos: false), composed);

        Assert.NotNull(await ReadAsync(file, composed.Normalize(NormalizationForm.FormD)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(3 * 1024)]
    [InlineData(5000)]
    public async Task Any_length_goes_through_the_chunks(int length)
    {
        var content = new byte[length];
        Random.Shared.NextBytes(content);
        using var file = new MemoryStream();
        await using (var encrypting = await BackupCrypto.StartEncryptingAsync(file, Password, CancellationToken.None, chunkSize: 1024))
        {
            await encrypting.WriteAsync(content.AsMemory(0, length / 2));
            encrypting.Write(content.AsSpan(length / 2));
            await encrypting.FinishAsync(CancellationToken.None);
        }

        file.Position = 0;
        await using var decrypting = await BackupCrypto.StartDecryptingAsync(file, Password, CancellationToken.None);
        using var result = new MemoryStream();
        await decrypting.CopyToAsync(result);

        Assert.Equal(content, result.ToArray());
    }

    [Fact]
    public async Task A_backup_that_was_not_finished_is_refused()
    {
        using var file = new MemoryStream();
        await using (var encrypting = await BackupCrypto.StartEncryptingAsync(file, Password, CancellationToken.None, chunkSize: 1024))
            await encrypting.WriteAsync(new byte[3000]);

        file.Position = 0;
        await using var decrypting = await BackupCrypto.StartDecryptingAsync(file, Password, CancellationToken.None);
        var error = await Assert.ThrowsAsync<FonteException>(() => decrypting.CopyToAsync(Stream.Null));
        Assert.Equal(FonteError.BackupDamaged, error.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private async Task<BodyPhoto> AddPhotoAsync(DateTime date, int size)
    {
        var fileName = $"{Guid.NewGuid():N}.jpg";
        var picture = new byte[size];
        Random.Shared.NextBytes(picture);
        Directory.CreateDirectory(PhotoFolder);
        await File.WriteAllBytesAsync(Path.Combine(PhotoFolder, fileName), picture);
        return await Store.AddPhotoAsync(date, fileName);
    }

    private async Task<byte[]> WriteAsync(BackupData data, string password = Password)
    {
        using var file = new MemoryStream();
        await BackupFile.WriteAsync(file, new BackupContent(ExportedAt, data, Settings), PhotoFolder, password, CancellationToken.None);
        return file.ToArray();
    }

    private Task<BackupContent> ReadAsync(byte[] file, string password = Password) =>
        BackupFile.ReadAsync(new MemoryStream(file), password, StagingFolder, CancellationToken.None);

    private async Task AssertFailsAsync(FonteError expected, byte[] file, string password = Password)
    {
        var error = await Assert.ThrowsAsync<FonteException>(() => ReadAsync(file, password));
        Assert.Equal(expected, error.Error);
    }

    private static byte[] Flip(byte[] file, int index)
    {
        var copy = (byte[])file.Clone();
        copy[index] ^= 1;
        return copy;
    }

    /// <summary>A second database, as on the phone the backup is restored on.</summary>
    private sealed class NewPhone : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"fonte-new-{Guid.NewGuid():N}.db3");

        public NewPhone() => Store = new FonteStore(_path);

        public FonteStore Store { get; }

        public async ValueTask DisposeAsync()
        {
            await Store.DisposeAsync();
            foreach (var file in Directory.GetFiles(Path.GetDirectoryName(_path)!, Path.GetFileName(_path) + "*"))
                File.Delete(file);
        }
    }
}
