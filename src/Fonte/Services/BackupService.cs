using Fonte.Core.Backup;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.ViewModels;
using Microsoft.Extensions.Logging;

namespace Fonte.Services;

/// <summary>
/// Saves everything to a password-encrypted file the user keeps where they like (iCloud Drive, mail…), and
/// restores one on another device. Each step tells the user what went wrong itself.
/// </summary>
public sealed class BackupService(
    FonteStore store,
    AppSettings settings,
    IDialogService dialogs,
    DeviceAuthentication authentication,
    BackupFilePicker files,
    PasswordPrompt passwords,
    ILogger<BackupService> logger)
{
    /// <summary>Pictures of a backup being restored wait here until the user confirms.</summary>
    private static string StagingFolder => Path.Combine(FileSystem.CacheDirectory, "restore-photos");

    /// <summary>
    /// Asks whether to include the photos, then for a password, writes the backup and hands it over. True once it
    /// was saved or sent.
    /// </summary>
    public async Task<bool> BackUpAsync()
    {
        try
        {
            if (await AskForPhotosAsync() is not { } includePhotos)
                return false;

            var now = DateTimeOffset.Now;
            var content = new BackupContent(now, await store.ExportAsync(includePhotos), CurrentSettings());
            DeleteOldBackups();
            var path = Path.Combine(FileSystem.CacheDirectory, $"fonte-{now:yyyy-MM-dd}{BackupFile.Extension}");

            var password = await passwords.AskAsync(PasswordPromptMode.Create, async chosen =>
            {
                try
                {
                    // Key derivation is deliberately slow, and photos take a while: keep it off the UI thread.
                    await Task.Run(async () =>
                    {
                        await using var file = File.Create(path);
                        await BackupFile.WriteAsync(file, content, PhotoStore.Folder, chosen, CancellationToken.None);
                    });
                    return null;
                }
                catch (Exception ex)
                {
                    // Shown in the sheet, which stays open: an exception here would leave it stuck.
                    logger.LogError(ex, "Could not write the backup");
                    return Loc.Format("Backup_NotCreated", ex.Message);
                }
            });
            if (password is null || !await files.HandOverAsync(path, Loc.Get("Backup_ShareTitle")))
                return false;

            settings.LastBackupAt = now;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup failed");
            await dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Format("Backup_NotCreated", ex.Message));
            return false;
        }
    }

    /// <summary>
    /// Lets the user pick a backup and unlock it, shows what it holds, and once confirmed replaces everything on
    /// this device with it. True once restored.
    /// </summary>
    public async Task<bool> RestoreAsync()
    {
        string? path = null;
        try
        {
            path = await files.OpenAsync();
            if (path is null)
                return false;
            await using (var file = File.OpenRead(path))
                BackupFile.CheckHeader(file);

            BackupContent? content = null;
            var password = await passwords.AskAsync(PasswordPromptMode.Unlock, async candidate =>
            {
                try
                {
                    // The whole file is decrypted and checked now, before anything on this device is replaced.
                    content = await Task.Run(async () =>
                    {
                        ClearStaging();
                        await using var file = File.OpenRead(path);
                        return await BackupFile.ReadAsync(file, candidate, StagingFolder, CancellationToken.None);
                    });
                    return null;
                }
                catch (FonteException ex)
                {
                    return Loc.Error(ex);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not read the backup");
                    return Loc.Format("Backup_NotRead", ex.Message);
                }
            });
            if (password is null || content is null)
                return false;

            if (!await dialogs.ConfirmAsync(Loc.Get("Restore_Title"), Describe(content), Loc.Get("Restore_Accept")))
                return false;

            await store.RestoreAsync(content.Data);
            if (content.Data.Photos is { } photos)
                ReplacePhotos(photos);
            Apply(content.Settings);
            settings.HasOnboarded = true;
            Palette.Haptic();

            await dialogs.AlertAsync(Loc.Get("Restore_DoneTitle"), Loc.Get("Restore_DoneText"));
            return true;
        }
        catch (FonteException ex)
        {
            await dialogs.AlertAsync(Loc.Get("Restore_FailedTitle"), Loc.Error(ex));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Restore failed");
            await dialogs.AlertAsync(Loc.Get("Restore_FailedTitle"), Loc.Format("Backup_NotRead", ex.Message));
            return false;
        }
        finally
        {
            ClearStaging();
            if (path is not null)
                TryDelete(path);
        }
    }

    /// <summary>
    /// Whether the backup takes the photos along; null when the user cancelled. In the app the photos are behind
    /// Face ID: taking them out of it asks for it too.
    /// </summary>
    private async Task<bool?> AskForPhotosAsync()
    {
        var photos = await store.GetPhotosAsync();
        if (photos.Count == 0)
            return false;

        var bytes = photos.Sum(p => File.Exists(PhotoStore.PathOf(p.FileName)) ? new FileInfo(PhotoStore.PathOf(p.FileName)).Length : 0);
        var with = Loc.Format("Backup_WithPhotos", Math.Max(1, (int)Math.Ceiling(bytes / 1_000_000d)));
        var without = Loc.Get("Backup_WithoutPhotos");
        var choice = await dialogs.ChooseAsync(Loc.Get("Backup_PhotosTitle"), null, with, without);
        if (choice is null)
            return null;
        if (choice == without)
            return false;
        return await authentication.AuthenticateAsync(Loc.Get("Backup_PhotosReason")) ? true : null;
    }

    private static string Describe(BackupContent content)
    {
        var date = Loc.Date(content.ExportedAt.LocalDateTime, "d MMMM yyyy");
        var workouts = Loc.Count(content.Data.Workouts.Count(w => w.FinishedAt is not null), "Workout");
        return content.Data.Photos is { } photos
            ? Loc.Format("Restore_TextWithPhotos", date, workouts, Loc.Count(photos.Count, "Photo"))
            : Loc.Format("Restore_TextWithoutPhotos", date, workouts);
    }

    /// <summary>The restored photos take the place of the current ones, whose rows are gone.</summary>
    private static void ReplacePhotos(IEnumerable<BodyPhoto> photos)
    {
        Directory.CreateDirectory(PhotoStore.Folder);
        var restored = photos.Select(p => p.FileName).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(PhotoStore.Folder))
        {
            if (!restored.Contains(Path.GetFileName(file)))
                TryDelete(file);
        }
        foreach (var name in restored)
        {
            var staged = Path.Combine(StagingFolder, name);
            if (File.Exists(staged))
                File.Move(staged, PhotoStore.PathOf(name), overwrite: true);
        }
    }

    private BackupSettings CurrentSettings() => new(
        settings.RestTimerEnabled,
        settings.RestSeconds,
        settings.Goal,
        settings.SessionsPerWeek,
        settings.GoalWeight,
        settings.BarWeight,
        settings.Plates.ToList(),
        settings.Accent.Key);

    /// <summary>The backup's preferences, except values this version would not understand. The language stays.</summary>
    private void Apply(BackupSettings saved)
    {
        settings.RestTimerEnabled = saved.RestTimerEnabled;
        if (saved.RestSeconds > 0)
            settings.RestSeconds = saved.RestSeconds;
        if (Enum.IsDefined(saved.Goal))
            settings.Goal = saved.Goal;
        settings.SessionsPerWeek = saved.SessionsPerWeek;
        settings.GoalWeight = saved.GoalWeight;
        if (saved.BarWeight > 0)
            settings.BarWeight = saved.BarWeight;
        if (saved.Plates?.Where(p => p > 0).ToList() is { Count: > 0 } plates)
            settings.Plates = plates;
        settings.Accent = AccentTheme.Find(saved.Accent);
    }

    /// <summary>
    /// The file handed over last time stays in the cache: on Android the app it was shared with may still be
    /// reading it. It is deleted at the next backup.
    /// </summary>
    private static void DeleteOldBackups()
    {
        foreach (var file in Directory.GetFiles(FileSystem.CacheDirectory, "fonte-*" + BackupFile.Extension))
            TryDelete(file);
    }

    private static void ClearStaging()
    {
        try
        {
            if (Directory.Exists(StagingFolder))
                Directory.Delete(StagingFolder, recursive: true);
        }
        catch (IOException)
        {
            // Cleared again before the next restore.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover in the cache, which the system clears when it needs room.
        }
    }
}
