using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Controls;
using Fonte.Core.Backup;
using Fonte.Localization;

namespace Fonte.ViewModels;

public enum PasswordPromptMode
{
    /// <summary>Choose a password to encrypt a new backup (typed twice).</summary>
    Create,

    /// <summary>Enter the password of a backup to restore.</summary>
    Unlock,
}

/// <param name="validate">Runs the slow work (encrypting, decrypting); returns an error message to show, or null on success.</param>
public sealed partial class BackupPasswordViewModel(
    PasswordPromptMode mode, Func<string, Task<string?>> validate, TaskCompletionSource<string?> result)
    : ObservableObject, ISheetViewModel
{
    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public bool IsCreate => mode == PasswordPromptMode.Create;

    public string Title => IsCreate ? Loc.Get("Password_CreateTitle") : Loc.Get("Password_UnlockTitle");

    public string Explanation => IsCreate
        ? Loc.Format("Password_CreateText", BackupFile.MinPasswordLength)
        : Loc.Get("Password_UnlockText");

    public string ConfirmText => IsCreate ? Loc.Get("Password_CreateConfirm") : Loc.Get("Password_UnlockConfirm");

    public string VisibilityIcon => IsHidden ? Icons.Eye : Icons.EyeOff;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibilityIcon))]
    private bool _isHidden = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    partial void OnPasswordChanged(string value) => HasError = false;

    partial void OnConfirmationChanged(string value) => HasError = false;

    [RelayCommand]
    private void ToggleVisibility() => IsHidden = !IsHidden;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (IsBusy)
            return;

        if (IsCreate && Password.Length < BackupFile.MinPasswordLength)
        {
            ShowError(Loc.Format("Password_TooShort", BackupFile.MinPasswordLength));
            return;
        }
        if (IsCreate && Password != Confirmation)
        {
            ShowError(Loc.Get("Password_Mismatch"));
            return;
        }
        if (Password.Length == 0)
            return;

        IsBusy = true;
        try
        {
            if (await validate(Password) is { } error)
            {
                ShowError(error);
                return;
            }
        }
        finally
        {
            IsBusy = false;
        }

        result.TrySetResult(Password);
    }

    [RelayCommand]
    private void Cancel() => result.TrySetResult(null);

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
