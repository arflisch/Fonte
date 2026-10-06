using Fonte.ViewModels;
using Fonte.Views;

namespace Fonte.Services;

/// <summary>Shows the backup password sheet and waits for the user.</summary>
public sealed class PasswordPrompt
{
    /// <summary>
    /// Returns the accepted password, or null if the user cancelled. <paramref name="validate"/> runs while the
    /// sheet is open (with a progress indicator) and can reject the password with a message, e.g. when it is wrong.
    /// </summary>
    public async Task<string?> AskAsync(PasswordPromptMode mode, Func<string, Task<string?>> validate)
    {
        var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation
            ?? throw new InvalidOperationException("No window is open.");

        var result = new TaskCompletionSource<string?>();
        var page = new BackupPasswordPage(new BackupPasswordViewModel(mode, validate, result));
        // Swiping the sheet down counts as cancelling.
        page.Disappearing += (_, _) => result.TrySetResult(null);

        await PresentationGuard.WaitUntilSettledAsync();
        await navigation.PushModalAsync(page);
        var password = await result.Task;

        if (navigation.ModalStack.Contains(page))
            await navigation.PopModalAsync();
        await PresentationGuard.WaitUntilSettledAsync();
        return password;
    }
}
