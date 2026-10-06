using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Catalog;
using Fonte.Core.Data;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>
/// First launch, in three steps: what Fonte does, the training goal and workouts per week, then a ready-made
/// program that fits them, to follow or not.
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    private const int StepCount = 3;

    private readonly FonteStore _store;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly BackupService _backup;
    private bool _isClosing;

    public WelcomeViewModel(FonteStore store, AppSettings settings, IDialogService dialogs, BackupService backup)
    {
        _store = store;
        _settings = settings;
        _dialogs = dialogs;
        _backup = backup;
        Goals = ChoiceItem.Goals(SelectGoal);
        Sessions = SelectableOption.Grid(AppSettings.SessionChoices.Select(s => s.ToString(Loc.Culture)), AppSettings.SessionChoices.Count, SelectSessions);
        SelectGoal(Goals[1]);
        SelectSessions(Sessions[1]);
        ShowStep(1);
    }

    public IReadOnlyList<ChoiceItem> Goals { get; }

    public IReadOnlyList<SelectableOption> Sessions { get; }

    [ObservableProperty]
    private int _step;

    [ObservableProperty]
    private string _stepText = string.Empty;

    [ObservableProperty]
    private double _stepProgress;

    [ObservableProperty]
    private bool _isIntro;

    [ObservableProperty]
    private bool _isGoal;

    [ObservableProperty]
    private bool _isProgram;

    [ObservableProperty]
    private string _programName = string.Empty;

    [ObservableProperty]
    private string _programText = string.Empty;

    [ObservableProperty]
    private string _programPlan = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _programTemplates = [];

    private TrainingGoal Goal => Enum.Parse<TrainingGoal>(Goals.First(g => g.IsSelected).Value);

    private int SessionsPerWeek => AppSettings.SessionChoices[Sessions.ToList().FindIndex(s => s.IsSelected)];

    private void SelectGoal(ChoiceItem goal)
    {
        foreach (var item in Goals)
            item.IsSelected = item == goal;
        Palette.Haptic();
    }

    private void SelectSessions(SelectableOption option)
    {
        foreach (var item in Sessions)
            item.IsSelected = item == option;
        Palette.Haptic();
    }

    private void ShowStep(int step)
    {
        Step = step;
        StepText = Loc.Format("Welcome_Step", step, StepCount);
        StepProgress = (double)step / StepCount;
        IsIntro = step == 1;
        IsGoal = step == 2;
        IsProgram = step == 3;
        if (IsProgram)
        {
            var program = ProgramCatalog.Suggest(Goal, SessionsPerWeek);
            ProgramName = Loc.Get($"Program_{program.Key}");
            ProgramText = Loc.Get($"ProgramText_{program.Key}");
            ProgramPlan = Loc.Format("Welcome_ProgramPlan", program.SessionsPerWeek, program.Weeks);
            ProgramTemplates = program.Templates
                .Select(t => $"{Loc.Get($"Template_{t.Key}")} · {Loc.Count(t.Exercises.Count, "Exercise")}")
                .ToList();
        }
    }

    [RelayCommand]
    private void Next()
    {
        if (Step < StepCount)
            ShowStep(Step + 1);
        Palette.Haptic();
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 1)
            ShowStep(Step - 1);
    }

    /// <summary>A new phone: bring back everything from a backup instead of starting afresh.</summary>
    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (_isClosing)
            return;
        _isClosing = true;
        try
        {
            if (await _backup.RestoreAsync())
                await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _isClosing = false;
        }
    }

    [RelayCommand]
    private Task FollowAsync() => FinishAsync(follow: true);

    [RelayCommand]
    private Task SkipAsync() => FinishAsync(follow: false);

    private async Task FinishAsync(bool follow)
    {
        if (_isClosing)
            return;
        _isClosing = true;
        try
        {
            var goal = Goal;
            _settings.Goal = goal;
            _settings.SessionsPerWeek = SessionsPerWeek;
            _settings.RestSeconds = ProgramCatalog.RestSeconds(goal);
            if (follow)
                await _store.CreateProgramFromCatalogAsync(ProgramCatalog.Suggest(goal, SessionsPerWeek), Loc.Get, DateTime.Now);
            _settings.HasOnboarded = true;
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), ex.Message);
        }
        finally
        {
            _isClosing = false;
        }
    }
}
