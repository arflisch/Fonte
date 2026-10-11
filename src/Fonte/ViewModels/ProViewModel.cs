using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>What Fonte Pro adds, and its one-time purchase (or restoring it).</summary>
public sealed partial class ProViewModel : ObservableObject, ISheetViewModel
{
    private readonly ProService _pro;
    private readonly IDialogService _dialogs;

    public ProViewModel(ProService pro, IDialogService dialogs)
    {
        _pro = pro;
        _dialogs = dialogs;
        _isUnlocked = pro.IsUnlocked;
        _buyText = Loc.Get("Pro_Unlock");
        Features =
        [
            new("📈", "#261D9E75", Loc.Get("Pro_ProgressionTitle"), Loc.Get("Pro_ProgressionText")),
            new("🗓️", "#26534AB7", Loc.Get("Pro_ProgramsTitle"), Loc.Get("Pro_ProgramsText")),
            new("📊", "#26378ADD", Loc.Get("Pro_ProgressTitle"), Loc.Get("Pro_ProgressText")),
            new("📏", "#26D4537E", Loc.Get("Pro_BodyTitle"), Loc.Get("Pro_BodyText")),
            new("🔥", "#26EF6C27", Loc.Get("Pro_SetsTitle"), Loc.Get("Pro_SetsText")),
            new("🏋️", "#26BA7517", Loc.Get("Pro_PlatesTitle"), Loc.Get("Pro_PlatesText")),
            .. HealthService.IsSupported ? [new ProFeature("❤️", "#26E24B4A", Loc.Get("Pro_HealthTitle"), Loc.Get("Pro_HealthText"))] : Array.Empty<ProFeature>(),
            new("📄", "#260891B2", Loc.Get("Pro_ExportTitle"), Loc.Get("Pro_ExportText")),
            new("🎨", "#26639922", Loc.Get("Pro_ColorsTitle"), Loc.Get("Pro_ColorsText")),
        ];
        if (!pro.IsUnlocked)
            _ = LoadPriceAsync();
    }

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public IReadOnlyList<ProFeature> Features { get; }

    public bool IsTestBuild => ProService.IsTestBuild;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    private bool _isUnlocked;

    public bool IsLocked => !IsUnlocked;

    /// <summary>"Unlock for €7.99" once the store gave the price.</summary>
    [ObservableProperty]
    private string _buyText;

    [ObservableProperty]
    private bool _isBusy;

    private async Task LoadPriceAsync()
    {
        if (await _pro.GetPriceAsync() is { } price)
            BuyText = Loc.Format("Pro_UnlockFor", price);
    }

    [RelayCommand]
    private async Task BuyAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            switch (await _pro.PurchaseAsync())
            {
                case ProPurchaseOutcome.Unlocked:
                    await UnlockedAsync(Loc.Get("Pro_Thanks"));
                    break;
                case ProPurchaseOutcome.Unavailable:
                    await _dialogs.AlertAsync(Loc.Get("Pro_UnavailableTitle"), Loc.Get("Pro_UnavailableText"));
                    break;
                case ProPurchaseOutcome.Failed:
                    await _dialogs.AlertAsync(Loc.Get("Pro_FailedTitle"), Loc.Get("Pro_FailedText"));
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            if (await _pro.RestoreAsync())
                await UnlockedAsync(Loc.Get("Pro_Restored"));
            else
                await _dialogs.AlertAsync(Loc.Get("Pro_NothingToRestoreTitle"), Loc.Get("Pro_NothingToRestoreText"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnlockForTestingAsync()
    {
#if FONTE_PRO_TESTING
        _pro.SetUnlockedForTesting(true);
        await UnlockedAsync(Loc.Get("Pro_Thanks"));
#else
        await Task.CompletedTask;
#endif
    }

    [RelayCommand]
    private void LockForTesting()
    {
#if FONTE_PRO_TESTING
        _pro.SetUnlockedForTesting(false);
        IsUnlocked = false;
#endif
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");

    private async Task UnlockedAsync(string message)
    {
        IsUnlocked = true;
        SuccessToast.Show(message);
        await Shell.Current.GoToAsync("..");
    }
}

/// <param name="Tint">Background of the emoji, a translucent colour ("#26…").</param>
public sealed record ProFeature(string Emoji, string Tint, string Title, string Text)
{
    public Color TintColor { get; } = Color.FromArgb(Tint);
}
