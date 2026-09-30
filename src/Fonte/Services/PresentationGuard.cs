namespace Fonte.Services;

public static class PresentationGuard
{
    /// <summary>
    /// iOS silently drops anything presented (alert, sheet, share sheet) while another controller — a file
    /// picker, a closing sheet — is still animating, leaving the awaiting code stuck. Wait for it to settle.
    /// </summary>
    public static async Task WaitUntilSettledAsync()
    {
#if IOS || MACCATALYST
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var top = Platform.GetCurrentUIViewController();
            // The document picker closes itself right after reporting the chosen file.
            var settling = top is not null
                && (top.IsBeingDismissed || top.IsBeingPresented || top is UIKit.UIDocumentPickerViewController);
            if (!settling)
                return;
            await Task.Delay(50);
        }
#else
        await Task.CompletedTask;
#endif
    }
}
