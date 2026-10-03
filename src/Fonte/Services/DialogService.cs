using Fonte.Localization;

namespace Fonte.Services;

public interface IDialogService
{
    Task AlertAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message, string accept, string? cancel = null);

    /// <summary>Shows an action sheet and returns the chosen option, or null when cancelled.</summary>
    Task<string?> ChooseAsync(string title, string? destructive, params string[] options);

    /// <summary>Asks for a short text; returns null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string placeholder, string initialValue, int maxLength);

    /// <summary>Asks for a number ("62.5" or "62,5"); returns null when cancelled or not a number.</summary>
    Task<double?> PromptNumberAsync(string title, string? message, double? initialValue);
}

public sealed class DialogService : IDialogService
{
    private static string Cancel => Loc.Get("Common_Cancel");

    public async Task AlertAsync(string title, string message)
    {
        await PresentationGuard.WaitUntilSettledAsync();
        await CurrentPage.DisplayAlertAsync(title, message, Loc.Get("Common_Ok"));
    }

    public async Task<bool> ConfirmAsync(string title, string message, string accept, string? cancel = null)
    {
        await PresentationGuard.WaitUntilSettledAsync();
        return await CurrentPage.DisplayAlertAsync(title, message, accept, cancel ?? Cancel);
    }

    public async Task<string?> ChooseAsync(string title, string? destructive, params string[] options)
    {
        await PresentationGuard.WaitUntilSettledAsync();
        var cancel = Cancel;
        var choice = await CurrentPage.DisplayActionSheetAsync(title, cancel, destructive, options);
        return choice is null || choice == cancel ? null : choice;
    }

    public async Task<string?> PromptAsync(string title, string placeholder, string initialValue, int maxLength)
    {
        await PresentationGuard.WaitUntilSettledAsync();
        return await CurrentPage.DisplayPromptAsync(title, null, Loc.Get("Common_Ok"), Cancel, placeholder, maxLength, Keyboard.Text, initialValue);
    }

    public async Task<double?> PromptNumberAsync(string title, string? message, double? initialValue)
    {
        await PresentationGuard.WaitUntilSettledAsync();
        var text = await CurrentPage.DisplayPromptAsync(title, message, Loc.Get("Common_Ok"), Cancel, "0", 6, Keyboard.Numeric,
            initialValue is { } value ? Loc.Number(value) : string.Empty);
        return double.TryParse(text?.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number > 0
            ? number
            : null;
    }

    /// <summary>The top-most page, including modal sheets, so dialogs appear above them.</summary>
    private static Page CurrentPage
    {
        get
        {
            var root = Application.Current?.Windows.FirstOrDefault()?.Page
                ?? throw new InvalidOperationException("No window is open.");
            return root.Navigation.ModalStack.LastOrDefault() ?? Shell.Current?.CurrentPage ?? root;
        }
    }
}
