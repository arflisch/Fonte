#if IOS || MACCATALYST
using Foundation;
using UIKit;
using UniformTypeIdentifiers;
#endif
using Fonte.Core.Backup;

namespace Fonte.Services;

/// <summary>Hands a new backup file over to the user, and lets them pick one to restore.</summary>
public sealed class BackupFilePicker(IFilePicker filePicker, IShare share)
{
    /// <summary>Each password attempt reads the file again: it is kept here, the app's own copy, until the restore ends.</summary>
    private static string RestorePath => Path.Combine(FileSystem.CacheDirectory, "restore" + BackupFile.Extension);

    /// <summary>Lets the user decide where the file goes: the share sheet, or the Save panel on a Mac.</summary>
    public async Task<BackupOutcome> HandOverAsync(string path, string title)
    {
        await PresentationGuard.WaitUntilSettledAsync();
#if MACCATALYST
        // On a Mac, a Save panel is expected rather than a share menu.
        _ = (share, title);
        var result = new TaskCompletionSource<bool>();
        var picker = new UIDocumentPickerViewController([NSUrl.FromFilename(path)], asCopy: true);
        picker.DidPickDocumentAtUrls += (_, _) => result.TrySetResult(true);
        picker.WasCancelled += (_, _) => result.TrySetResult(false);
        Present(picker);
        return await result.Task ? BackupOutcome.Saved : BackupOutcome.Cancelled;
#elif IOS
        // Unlike MAUI's share, the native sheet says whether the file was saved or sent, or the sheet closed.
        _ = (share, title);
        var result = new TaskCompletionSource<bool>();
        var sheet = new UIActivityViewController([NSUrl.FromFilename(path)], null)
        {
            CompletionWithItemsHandler = (_, completed, _, _) => result.TrySetResult(completed),
        };
        if (sheet.PopoverPresentationController is { } popover && Platform.GetCurrentUIViewController()?.View is { } view)
        {
            // iPad: anchor the popover in the middle of the screen.
            popover.SourceView = view;
            popover.SourceRect = new CoreGraphics.CGRect(view.Bounds.Width / 2, view.Bounds.Height / 2, 0, 0);
            popover.PermittedArrowDirections = 0;
        }
        Present(sheet);
        return await result.Task ? BackupOutcome.Saved : BackupOutcome.Cancelled;
#else
        await share.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path, "application/octet-stream") });
        // Android does not tell whether the file actually went anywhere.
        return BackupOutcome.HandedOver;
#endif
    }

    /// <summary>Lets the user pick a backup file; returns the path of a local copy, or null if they cancelled.</summary>
    public async Task<string?> OpenAsync()
    {
        await PresentationGuard.WaitUntilSettledAsync();
#if IOS || MACCATALYST
        // MAUI's picker loses the chosen file on iOS 26: its "sheet dismissed" handler reports a cancellation
        // before the asynchronous file access completes. Picking a copy, completed in the pick callback, avoids it.
        _ = filePicker;
        var result = new TaskCompletionSource<string?>();
        // Backups have no registered type: any file can be picked, and its header tells whether it is one.
        var picker = new UIDocumentPickerViewController([UTTypes.Data], asCopy: true) { AllowsMultipleSelection = false };
        picker.DidPickDocumentAtUrls += (_, e) =>
        {
            // The picked copy lands in a temporary inbox that the system empties on its own, even while the
            // password is being typed: move it to the cache at once (same volume, so it is only a rename).
            try
            {
                if (e.Urls.FirstOrDefault()?.Path is not { } picked)
                {
                    result.TrySetResult(null);
                    return;
                }
                File.Move(picked, RestorePath, overwrite: true);
                result.TrySetResult(RestorePath);
            }
            catch (Exception ex)
            {
                result.TrySetException(ex);
            }
        };
        picker.WasCancelled += (_, _) => result.TrySetResult(null);
        if (picker.PresentationController is { } presentation)
            presentation.Delegate = new SwipeDismissDelegate(() => result.TrySetResult(null));
        Present(picker);
        return await result.Task;
#else
        var file = await filePicker.PickAsync();
        if (file is null)
            return null;

        await using var source = await file.OpenReadAsync();
        await using var target = File.Create(RestorePath);
        await source.CopyToAsync(target);
        return RestorePath;
#endif
    }

#if IOS || MACCATALYST
    private static void Present(UIViewController controller)
    {
        var presenter = Platform.GetCurrentUIViewController()
            ?? throw new InvalidOperationException("No view controller to present from.");
        presenter.PresentViewController(controller, true, null);
    }

    /// <summary>Treats a swipe-down on the picker sheet as a cancellation.</summary>
    private sealed class SwipeDismissDelegate(Action onDismissed) : UIAdaptivePresentationControllerDelegate
    {
        public override void DidDismiss(UIPresentationController presentationController) => onDismissed();
    }
#endif
}

public enum BackupOutcome
{
    /// <summary>The user closed the share sheet or the Save panel.</summary>
    Cancelled,

    /// <summary>The file was saved or sent.</summary>
    Saved,

    /// <summary>The file was handed to the system share menu, which does not report what happened next.</summary>
    HandedOver,
}
