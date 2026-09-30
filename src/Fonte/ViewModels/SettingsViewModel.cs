using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, ISheetViewModel
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(AppSettings settings, IDialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        _languageName = settings.Language.NativeName;
        _restText = Loc.Seconds(settings.RestSeconds);
        _isRestTimerEnabled = settings.RestTimerEnabled;
    }

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public string AppVersion => $"Fonte {AppInfo.Current.VersionString}";

    [ObservableProperty]
    private string _languageName;

    [ObservableProperty]
    private string _restText;

    [ObservableProperty]
    private bool _isRestTimerEnabled;

    partial void OnIsRestTimerEnabledChanged(bool value)
    {
        _settings.RestTimerEnabled = value;
        Palette.Haptic();
    }

    [RelayCommand]
    private async Task ChooseLanguageAsync()
    {
        var current = _settings.Language;
        var options = Localizer.Languages.Select(l => l == current ? $"{l.NativeName}  ✓" : l.NativeName).ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Settings_Language"), null, options);
        var language = Localizer.Languages.FirstOrDefault(l => choice?.StartsWith(l.NativeName, StringComparison.Ordinal) == true);
        if (language is null || language == current)
            return;

        _settings.Language = language;
        LanguageName = language.NativeName;
        RestText = Loc.Seconds(_settings.RestSeconds);
    }

    [RelayCommand]
    private async Task ChooseRestAsync()
    {
        var current = _settings.RestSeconds;
        var options = AppSettings.RestChoices
            .Select(s => s == current ? $"{Loc.Seconds(s)}  ✓" : Loc.Seconds(s))
            .ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Settings_Rest"), null, options);
        if (choice is null)
            return;

        var seconds = AppSettings.RestChoices.First(s => choice.Replace("✓", string.Empty).Trim() == Loc.Seconds(s));
        _settings.RestSeconds = seconds;
        RestText = Loc.Seconds(seconds);
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}
