using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Catalog;
using Fonte.Core.Training;
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
        _goalName = NameOf(settings.Goal);
        _sessionsText = Loc.Format("Settings_SessionsValue", settings.SessionsPerWeek);
        var available = settings.Plates;
        PlateOptions = Plates.Standard
            .Select((p, i) => new SelectableOption(p.ToString(System.Globalization.CultureInfo.InvariantCulture), i, int.MaxValue, TogglePlate)
            {
                Label = Loc.Number(p),
                IsSelected = available.Contains(p),
            })
            .ToList();
    }

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public string AppVersion => $"Fonte {AppInfo.Current.VersionString}";

    public IReadOnlyList<SelectableOption> PlateOptions { get; }

    [ObservableProperty]
    private string _languageName;

    [ObservableProperty]
    private string _restText;

    [ObservableProperty]
    private bool _isRestTimerEnabled;

    [ObservableProperty]
    private string _goalName;

    [ObservableProperty]
    private string _sessionsText;

    partial void OnIsRestTimerEnabledChanged(bool value)
    {
        _settings.RestTimerEnabled = value;
        Palette.Haptic();
    }

    private static string NameOf(TrainingGoal goal) => Loc.Get($"Goal_{goal}");

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
        GoalName = NameOf(_settings.Goal);
        SessionsText = Loc.Format("Settings_SessionsValue", _settings.SessionsPerWeek);
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
    private async Task ChooseGoalAsync()
    {
        var current = _settings.Goal;
        var goals = Enum.GetValues<TrainingGoal>();
        var options = goals.Select(g => g == current ? $"{NameOf(g)}  ✓" : NameOf(g)).ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Settings_Goal"), null, options);
        if (choice is null)
            return;
        var goal = goals.First(g => choice.Replace("✓", string.Empty).Trim() == NameOf(g));
        if (goal == current)
            return;

        _settings.Goal = goal;
        GoalName = NameOf(goal);
        // The rest that suits the new goal, unless the user chose another one than the old goal's.
        if (_settings.RestSeconds == ProgramCatalog.RestSeconds(current))
        {
            _settings.RestSeconds = ProgramCatalog.RestSeconds(goal);
            RestText = Loc.Seconds(_settings.RestSeconds);
        }
    }

    [RelayCommand]
    private async Task ChooseSessionsAsync()
    {
        var current = _settings.SessionsPerWeek;
        var options = AppSettings.SessionChoices
            .Select(s => Loc.Format("Settings_SessionsValue", s) + (s == current ? "  ✓" : string.Empty))
            .ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Settings_Sessions"), null, options);
        if (choice is null)
            return;
        var sessions = AppSettings.SessionChoices.First(s => choice.Replace("✓", string.Empty).Trim() == Loc.Format("Settings_SessionsValue", s));
        _settings.SessionsPerWeek = sessions;
        SessionsText = Loc.Format("Settings_SessionsValue", sessions);
    }

    private void TogglePlate(SelectableOption option)
    {
        // At least one plate must stay available.
        if (option.IsSelected && PlateOptions.Count(o => o.IsSelected) == 1)
            return;
        option.IsSelected = !option.IsSelected;
        _settings.Plates = PlateOptions
            .Where(o => o.IsSelected)
            .Select(o => double.Parse(o.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Palette.Haptic();
    }

    [RelayCommand]
    private Task OpenPlatesAsync() => Shell.Current.GoToAsync(Routes.Plates);

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}
