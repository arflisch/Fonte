using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fonte.Core.Catalog;
using Fonte.Core.Data;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, ISheetViewModel
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly FonteStore _store;
    private readonly ProService _pro;
    private readonly HealthService _health;
    private bool _isRevertingHealth;

    public SettingsViewModel(AppSettings settings, IDialogService dialogs, FonteStore store, ProService pro, HealthService health)
    {
        _settings = settings;
        _dialogs = dialogs;
        _store = store;
        _pro = pro;
        _health = health;
        _isPro = pro.IsUnlocked;
        _isHealthEnabled = settings.HealthEnabled && pro.IsUnlocked;
        // Fonte Pro can be unlocked from this sheet (its page opens on top of it).
        WeakReferenceMessenger.Default.Register<SettingsViewModel, DataChangedMessage>(this, static (vm, _) => vm.OnProChanged());
        _languageName = settings.Language.NativeName;
        _restText = Loc.Seconds(settings.RestSeconds);
        _isRestTimerEnabled = settings.RestTimerEnabled;
        _goalName = NameOf(settings.Goal);
        _sessionsText = Loc.Format("Settings_SessionsValue", settings.SessionsPerWeek);
        var accent = settings.Accent;
        _accentName = accent.Name;
        AccentOptions = AccentTheme.All
            .Select((a, i) => new SelectableOption(a.Key, i, AccentTheme.All.Count, SelectAccent)
            {
                Label = a.Name,
                Accent = a.Base,
                IsSelected = a == accent,
            })
            .ToList();
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

    /// <summary>The colours the app can take, as swatches.</summary>
    public IReadOnlyList<SelectableOption> AccentOptions { get; }

    public bool IsHealthSupported => HealthService.IsSupported;

    /// <summary>Fonte Pro is unlocked; otherwise its features show a "PRO" badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked), nameof(ProStatus))]
    private bool _isPro;

    public bool IsLocked => !IsPro;

    public string ProStatus => IsPro ? Loc.Get("Pro_StatusUnlocked") : Loc.Get("Pro_StatusLocked");

    [ObservableProperty]
    private bool _isHealthEnabled;

    [ObservableProperty]
    private string _accentName;

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
        AccentName = _settings.Accent.Name;
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

    private async void SelectAccent(SelectableOption option)
    {
        // Violet is free, the other colours come with Fonte Pro.
        if (option != AccentOptions[0] && !_pro.IsUnlocked)
        {
            await Shell.Current.GoToAsync(Routes.Pro);
            return;
        }
        foreach (var item in AccentOptions)
            item.IsSelected = item == option;
        var accent = AccentTheme.Find(option.Value);
        _settings.Accent = accent;
        AccentName = accent.Name;
        Palette.Haptic();
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
    private Task OpenPlatesAsync() => Shell.Current.GoToAsync(_pro.IsUnlocked ? Routes.Plates : Routes.Pro);

    [RelayCommand]
    private Task OpenProAsync() => Shell.Current.GoToAsync(Routes.Pro);

    async partial void OnIsHealthEnabledChanged(bool value)
    {
        if (_isRevertingHealth)
            return;
        if (!value)
        {
            _settings.HealthEnabled = false;
            return;
        }
        if (!_pro.IsUnlocked)
        {
            RevertHealth();
            await Shell.Current.GoToAsync(Routes.Pro);
            return;
        }
        if (!await _health.RequestAccessAsync())
        {
            RevertHealth();
            await _dialogs.AlertAsync(Loc.Get("Settings_Health"), Loc.Get("Settings_HealthDenied"));
            return;
        }
        _settings.HealthEnabled = true;
        Palette.Haptic();
        var added = await _health.ImportWeightsAsync();
        if (added > 0)
            SuccessToast.Show(Loc.Format("Settings_HealthImported", added));
    }

    private void OnProChanged()
    {
        IsPro = _pro.IsUnlocked;
        // Health only works with Fonte Pro: the switch follows.
        if (!IsPro && IsHealthEnabled)
            RevertHealth();
        else if (IsPro && _settings.HealthEnabled && !IsHealthEnabled)
        {
            _isRevertingHealth = true;
            IsHealthEnabled = true;
            _isRevertingHealth = false;
        }
    }

    private void RevertHealth()
    {
        _isRevertingHealth = true;
        IsHealthEnabled = false;
        _isRevertingHealth = false;
    }

    /// <summary>Every set of every workout, as a CSV file for Excel or Numbers (Fonte Pro).</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (!_pro.IsUnlocked)
        {
            await Shell.Current.GoToAsync(Routes.Pro);
            return;
        }
        var workouts = await _store.GetRecentWorkoutsAsync(int.MaxValue);
        if (workouts.Count == 0)
        {
            await _dialogs.AlertAsync(Loc.Get("Settings_Export"), Loc.Get("Settings_ExportEmpty"));
            return;
        }
        var texts = new CsvTexts(
            Loc.Get("Csv_Date"), Loc.Get("Csv_Time"), Loc.Get("Csv_Workout"), Loc.Get("Csv_Exercise"), Loc.Get("Csv_Muscle"),
            Loc.Get("Csv_Set"), Loc.Get("Csv_Kind"), Loc.Get("Csv_Weight"), Loc.Get("Csv_Reps"), Loc.Get("Csv_Seconds"),
            Loc.Get("Csv_Rpe"), Loc.Get("Csv_Note"),
            Loc.ExerciseName, Loc.Muscle, SetRowViewModel.KindName);
        var path = Path.Combine(FileSystem.CacheDirectory, $"Fonte-{DateTime.Now:yyyy-MM-dd}.csv");
        await File.WriteAllBytesAsync(path, WorkoutCsv.Write(workouts, texts, Loc.Culture));
        await PresentationGuard.WaitUntilSettledAsync();
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = Loc.Get("Settings_Export"),
            File = new ShareFile(path, "text/csv"),
        });
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}
