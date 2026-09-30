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
public sealed partial class ExerciseDetailViewModel(FonteStore store, IDialogService dialogs)
    : ReloadingViewModel, IQueryAttributable
{
    private int _exerciseId;

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
            history = await store.GetExerciseHistoryAsync(_exerciseId);
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

        // Oldest first, one point per workout: its best set.
        Points = history.Sessions
            .Reverse()
            .Select(s => s.Sets.Max(set => Performance.Score(exercise.Tracking, set)))
            .ToList();
        HasProgress = Points.Count >= 2;
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
                s.Sets.Any(set => set.Id == bestId)))
            .ToList();
        HasSessions = Sessions.Count > 0;
        IsEmpty = !HasSessions;
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task EditAsync() => Shell.Current.GoToAsync($"{Routes.EditExercise}?id={_exerciseId}");

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await dialogs.ConfirmAsync(
            Loc.Get("Detail_DeleteTitle"),
            Loc.Get("Detail_DeleteText"),
            Loc.Get("Detail_DeleteConfirm"));
        if (!confirmed)
            return;

        try
        {
            await store.RemoveExerciseAsync(_exerciseId);
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
        }
        catch (FonteException ex)
        {
            await dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }
}

/// <param name="HasRecord">The record was set during that workout.</param>
public sealed record SessionItem(string DayText, string SetsText, bool HasRecord);
