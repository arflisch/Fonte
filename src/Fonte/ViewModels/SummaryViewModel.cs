using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>What a workout achieved: shown when it is finished, and when it is opened from the history.</summary>
public sealed partial class SummaryViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    /// <summary>How the workout felt, from 1 (exhausting) to 5 (excellent).</summary>
    public static readonly IReadOnlyList<string> FeelingEmojis = ["😫", "😕", "😐", "🙂", "🤩"];

    /// <summary>Finished workouts after which Fonte Pro is suggested once, when nothing better shows it.</summary>
    private const int ProHintAfter = 3;

    private readonly FonteStore _store;
    private readonly IDialogService _dialogs;
    private readonly FinishedWorkouts _finished;
    private readonly ProService _pro;
    private readonly AppSettings _settings;
    private int _workoutId;
    private int? _feeling;

    public SummaryViewModel(FonteStore store, IDialogService dialogs, FinishedWorkouts finished, ProService pro, AppSettings settings)
    {
        _store = store;
        _dialogs = dialogs;
        _finished = finished;
        _pro = pro;
        _settings = settings;
        Feelings = SelectableOption.Grid(FeelingEmojis, FeelingEmojis.Count, SelectFeeling);
    }

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public IReadOnlyList<SelectableOption> Feelings { get; }

    /// <summary>Just finished (a celebration) rather than opened from the history.</summary>
    [ObservableProperty]
    private bool _isFresh;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _durationText = string.Empty;

    [ObservableProperty]
    private string _volumeText = string.Empty;

    [ObservableProperty]
    private string _setsText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<RecordItem> _records = [];

    [ObservableProperty]
    private bool _hasRecords;

    [ObservableProperty]
    private IReadOnlyList<RecapItem> _exercises = [];

    [ObservableProperty]
    private string _comparisonText = string.Empty;

    [ObservableProperty]
    private string _comparisonDetail = string.Empty;

    [ObservableProperty]
    private bool _hasComparison;

    [ObservableProperty]
    private bool _isBetter;

    [ObservableProperty]
    private IReadOnlyList<ProgressionItem> _progressions = [];

    [ObservableProperty]
    private bool _hasProgressions;

    /// <summary>Targets this workout succeeded, that Fonte Pro would have raised.</summary>
    [ObservableProperty]
    private bool _hasProTeaser;

    /// <summary>The one-time suggestion of Fonte Pro, after a few workouts.</summary>
    [ObservableProperty]
    private bool _hasProHint;

    [ObservableProperty]
    private string _feelingText = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var id) || !int.TryParse(id?.ToString(), out _workoutId))
            return;
        IsFresh = query.ContainsKey("fresh");
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Summary not loaded: {ex}");
        }
    }

    private async Task LoadAsync()
    {
        if (await _store.GetWorkoutSummaryAsync(_workoutId) is not { } summary)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        var workout = summary.Workout;
        var day = Loc.Date(workout.StartedAt, "dddd d MMMM");
        var hours = $"{Loc.Time(workout.StartedAt)} – {Loc.Time(workout.FinishedAt ?? workout.StartedAt)}";
        if (IsFresh)
        {
            Title = Loc.Get("Summary_TitleFresh");
            Subtitle = summary.TemplateName is { } name ? $"{name} · {day}" : day;
        }
        else
        {
            Title = summary.TemplateName ?? Loc.Day(workout.StartedAt);
            Subtitle = summary.TemplateName is null ? hours : $"{Loc.Day(workout.StartedAt)} · {hours}";
        }
        ShowComparison(summary);
        // Only finishing the workout tells which targets went up.
        var finished = IsFresh ? _finished.Take(_workoutId) : null;
        var steps = finished?.Progressions ?? [];
        Progressions = steps.Select(p => new ProgressionItem(Loc.ExerciseName(p.Exercise), ProgressionText(p))).ToList();
        HasProgressions = Progressions.Count > 0 && finished!.ProgressionsApplied;
        HasProTeaser = Progressions.Count > 0 && !finished!.ProgressionsApplied;
        HasProHint = false;
        if (IsFresh && !_pro.IsUnlocked && !HasProTeaser && !_settings.ProHintShown
            && (await _store.GetWorkoutDatesAsync()).Count >= ProHintAfter)
        {
            HasProHint = true;
            _settings.ProHintShown = true;
        }
        DurationText = Loc.Duration(summary.Duration);
        VolumeText = Loc.Volume(summary.Volume);
        SetsText = summary.SetCount.ToString(Loc.Culture);
        Records = summary.Records.Select(r => new RecordItem(Loc.ExerciseName(r.Exercise), Loc.Set(r.Exercise, r.Set), RecordDetail(r))).ToList();
        HasRecords = Records.Count > 0;
        Exercises = summary.Exercises
            .Select(e => new RecapItem(
                Loc.ExerciseName(e.Exercise),
                Palette.Muscle(e.Exercise.Muscle),
                string.Join("   ", e.Sets.Select(s => Loc.Set(e.Exercise, s)))))
            .ToList();
        Note = workout.Note ?? string.Empty;
        ShowFeeling(workout.Feeling);
    }

    /// <summary>"+4 % volume compared with last time", or more sets when nothing was loaded.</summary>
    private void ShowComparison(WorkoutSummary summary)
    {
        HasComparison = summary.Previous is not null;
        if (summary.Previous is not { } previous)
            return;

        double change;
        string what;
        if (previous.Volume > 0 && summary.Volume > 0)
        {
            change = (summary.Volume - previous.Volume) / previous.Volume;
            what = Loc.Get("Summary_CompareVolume");
        }
        else
        {
            change = previous.SetCount > 0 ? (double)(summary.SetCount - previous.SetCount) / previous.SetCount : 0;
            what = Loc.Get("Summary_CompareSets");
        }
        var percent = (int)Math.Round(change * 100);
        IsBetter = percent >= 0;
        ComparisonText = percent == 0 ? "=" : $"{(percent > 0 ? "+" : "−")}{Math.Abs(percent)} %";
        ComparisonDetail = Loc.Format("Summary_CompareWith", what, Loc.Day(previous.Workout.StartedAt).ToLower(Loc.Culture));
    }

    /// <summary>"60 → 62.5 kg", "8 → 9 reps", "45 s → 50 s".</summary>
    private static string ProgressionText(ProgressionStep step) => step.Kind switch
    {
        ProgressionKind.Weight => $"{Loc.Number(step.From)} → {Loc.Weight(step.To)}",
        ProgressionKind.Seconds => $"{Loc.Seconds((int)step.From)} → {Loc.Seconds((int)step.To)}",
        _ => $"{Loc.Number(step.From)} → {Loc.Number(step.To)} {Loc.Get("Unit_Reps")}",
    };

    /// <summary>"Est. 1RM 122.5 kg · +5.8" for loads, "Previous best: 12 reps" otherwise.</summary>
    private static string RecordDetail(PersonalRecord record)
    {
        if (record.Exercise.Tracking == Tracking.WeightAndReps && record.Set.Weight > 0)
        {
            var oneRepMax = Performance.EstimatedOneRepMax(record.Set.Weight, record.Set.Reps);
            return Loc.Format("Summary_RecordOneRepMax", Loc.Weight(Math.Round(oneRepMax, 1)), Loc.Number(Math.Round(oneRepMax - record.PreviousBest, 1)));
        }
        var before = record.Exercise.Tracking == Tracking.Time
            ? Loc.Seconds((int)record.PreviousBest)
            : $"{Loc.Number(record.PreviousBest)} {Loc.Get("Unit_Reps")}";
        return Loc.Format("Summary_RecordBefore", before);
    }

    private void SelectFeeling(SelectableOption option)
    {
        var feeling = Feelings.ToList().IndexOf(option) + 1;
        ShowFeeling(_feeling == feeling ? null : feeling);
        Palette.Haptic();
    }

    private void ShowFeeling(int? feeling)
    {
        _feeling = feeling;
        for (var i = 0; i < Feelings.Count; i++)
            Feelings[i].IsSelected = feeling == i + 1;
        FeelingText = feeling switch
        {
            1 => Loc.Get("Feeling_1"),
            2 => Loc.Get("Feeling_2"),
            3 => Loc.Get("Feeling_3"),
            4 => Loc.Get("Feeling_4"),
            5 => Loc.Get("Feeling_5"),
            _ => string.Empty,
        };
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveWorkoutReviewAsync(_workoutId, _feeling, Note);
            SuccessToast.Show(Loc.Get("Summary_Saved"));
            await Shell.Current.GoToAsync("..");
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            Loc.Get("Summary_DeleteTitle"),
            Loc.Get("Summary_DeleteText"),
            Loc.Get("Summary_Delete"));
        if (!confirmed)
            return;
        await _store.DeleteWorkoutAsync(_workoutId);
        Palette.Haptic();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task OpenProAsync() => Shell.Current.GoToAsync(Routes.Pro);
}

public sealed record RecordItem(string Name, string SetText, string Detail);

public sealed record RecapItem(string Name, Color Color, string SetsText);

/// <summary>A target of the template raised for next time.</summary>
public sealed record ProgressionItem(string Name, string Text);
