using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Controls;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>An exercise's record, its progress workout after workout, and every time it was done.</summary>
public sealed partial class ExerciseDetailViewModel : ReloadingViewModel, IQueryAttributable
{
    private readonly FonteStore _store;
    private readonly IDialogService _dialogs;
    private int _exerciseId;
    private ExerciseHistory? _history;
    private int _months;

    public ExerciseDetailViewModel(FonteStore store, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        PeriodChips =
        [
            new SelectableOption("3", 0, 3, SelectPeriod) { Label = Loc.Get("Detail_Period3Months") },
            new SelectableOption("12", 1, 3, SelectPeriod) { Label = Loc.Get("Detail_PeriodYear") },
            new SelectableOption("0", 2, 3, SelectPeriod) { Label = Loc.Get("Detail_PeriodAll"), IsSelected = true },
        ];
    }

    public IReadOnlyList<SelectableOption> PeriodChips { get; }

    [ObservableProperty]
    private string _chartStartText = string.Empty;

    [ObservableProperty]
    private string _chartEndText = string.Empty;

    [ObservableProperty]
    private string _tip = string.Empty;

    [ObservableProperty]
    private bool _hasTip;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _icon = Icons.Barbell;

    [ObservableProperty]
    private Color _color = Palette.Primary;

    [ObservableProperty]
    private Brush _heroBrush = Palette.HeroBrush(Palette.Primary);

    [ObservableProperty]
    private string _recordText = "—";

    [ObservableProperty]
    private string _oneRepMaxText = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordSpan))]
    private bool _showOneRepMax;

    /// <summary>Without an estimated one-rep max, the record takes its room.</summary>
    public int RecordSpan => ShowOneRepMax ? 1 : 2;

    [ObservableProperty]
    private string _sessionsText = "0";

    [ObservableProperty]
    private IReadOnlyList<double> _points = [];

    [ObservableProperty]
    private bool _hasProgress;

    [ObservableProperty]
    private string _progressHint = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SessionItem> _sessions = [];

    [ObservableProperty]
    private bool _hasSessions;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isCustom;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var exerciseId))
        {
            _exerciseId = exerciseId;
            RequestReload();
        }
    }

    protected override async Task LoadCoreAsync()
    {
        if (_exerciseId == 0)
            return;

        ExerciseHistory history;
        try
        {
            history = await _store.GetExerciseHistoryAsync(_exerciseId);
        }
        catch (FonteException)
        {
            return; // Deleted: the page is being closed.
        }

        var exercise = history.Exercise;
        Name = Loc.ExerciseName(exercise);
        Subtitle = $"{Loc.Muscle(exercise.Muscle)} · {Loc.Equipment(exercise.Equipment)}";
        Icon = Icons.For(exercise.Equipment);
        Color = Palette.Muscle(exercise.Muscle);
        HeroBrush = Palette.HeroBrush(Color);
        IsCustom = exercise.IsCustom;

        RecordText = history.Best is { } best ? Loc.Set(exercise, best) : "—";
        var oneRepMax = history.BestEstimatedOneRepMax;
        ShowOneRepMax = exercise.Tracking == Tracking.WeightAndReps;
        OneRepMaxText = oneRepMax > 0 ? Loc.Weight(Math.Round(oneRepMax, 1)) : "—";
        SessionsText = history.Sessions.Count.ToString(Loc.Culture);

        _history = history;
        ShowChart();
        HasProgress = history.Sessions.Count >= 2;
        Tip = exercise.CatalogKey is { } key ? Loc.Get($"Tip_{key}") : string.Empty;
        HasTip = Tip.Length > 0;
        ProgressHint = exercise.Tracking switch
        {
            Tracking.Time => Loc.Get("Detail_ProgressTime"),
            Tracking.Reps => Loc.Get("Detail_ProgressReps"),
            _ => Loc.Get("Detail_ProgressOneRepMax"),
        };

        var bestId = history.Best?.Id;
        // Two workouts the same day are told apart by their time.
        var sameDay = history.Sessions.GroupBy(s => s.Workout.StartedAt.Date).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        Sessions = history.Sessions
            .Select(s => new SessionItem(
                sameDay.Contains(s.Workout.StartedAt.Date)
                    ? $"{Loc.Day(s.Workout.StartedAt)} · {Loc.Time(s.Workout.StartedAt)}"
                    : Loc.Day(s.Workout.StartedAt),
                string.Join("   ", s.Sets.Select(set => Loc.Set(exercise, set))),
                s.Sets.Any(set => set.Id == bestId),
                Performance.Volume(s.Sets) is > 0 and var volume ? Loc.Format("Detail_Volume", Loc.Volume(volume)) : string.Empty))
            .ToList();
        HasSessions = Sessions.Count > 0;
        IsEmpty = !HasSessions;
    }

    private void SelectPeriod(SelectableOption chip)
    {
        foreach (var option in PeriodChips)
            option.IsSelected = option == chip;
        _months = int.Parse(chip.Value, Loc.Culture);
        Palette.Haptic();
        ShowChart();
    }

    /// <summary>Oldest first, one point per workout of the period: its best set.</summary>
    private void ShowChart()
    {
        if (_history is not { } history)
            return;
        var tracking = history.Exercise.Tracking;
        var from = _months > 0 ? DateTime.Today.AddMonths(-_months) : DateTime.MinValue;
        var points = history.Sessions
            .Where(s => s.Workout.StartedAt >= from)
            .Reverse()
            .Select(s => (s.Workout.StartedAt, Score: s.Sets.Max(set => Performance.Score(tracking, set))))
            .ToList();
        Points = points.Select(p => p.Score).ToList();
        ChartStartText = points.Count > 0 ? ChartLabel(tracking, points[0]) : string.Empty;
        ChartEndText = points.Count > 1 ? ChartLabel(tracking, points[^1]) : string.Empty;
    }

    /// <summary>"Oct 2025 · 78 kg".</summary>
    private static string ChartLabel(Tracking tracking, (DateTime Date, double Score) point)
    {
        var value = tracking switch
        {
            Tracking.Time => Loc.Seconds((int)point.Score),
            Tracking.Reps => $"{Loc.Number(point.Score)} {Loc.Get("Unit_Reps")}",
            _ => Loc.Weight(Math.Round(point.Score, 1)),
        };
        return $"{point.Date.ToString("MMM yyyy", Loc.Culture)} · {value}";
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task EditAsync() => Shell.Current.GoToAsync($"{Routes.EditExercise}?id={_exerciseId}");

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Loc.Get("Detail_DeleteTitle"),
            Loc.Get("Detail_DeleteText"),
            Loc.Get("Detail_DeleteConfirm"));
        if (!confirmed)
            return;

        try
        {
            await _store.RemoveExerciseAsync(_exerciseId);
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }
}

/// <param name="HasRecord">The record was set during that workout.</param>
public sealed record SessionItem(string DayText, string SetsText, bool HasRecord, string VolumeText)
{
    public bool HasVolume => VolumeText.Length > 0;
}
