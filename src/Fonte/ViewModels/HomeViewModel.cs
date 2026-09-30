using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>"Workouts" tab: start or resume a workout, this week at a glance, and the last workouts.</summary>
public sealed partial class HomeViewModel(FonteStore store) : ReloadingViewModel
{
    private const int RecentCount = 30;

    private Workout? _inProgress;

    [ObservableProperty]
    private string _todayText = string.Empty;

    [ObservableProperty]
    private bool _isInProgress;

    [ObservableProperty]
    private string _inProgressText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<WeekDayItem> _week = [];

    [ObservableProperty]
    private string _weekText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<WorkoutItemViewModel> _recent = [];

    [ObservableProperty]
    private bool _hasRecent;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isLoaded;

    protected override async Task LoadCoreAsync()
    {
        var now = DateTime.Now;
        TodayText = Loc.Date(now, "dddd d MMMM");

        _inProgress = await store.GetWorkoutInProgressAsync();
        if (_inProgress is { } workout)
        {
            var detail = await store.GetWorkoutDetailAsync(workout.Id);
            InProgressText = Loc.Format("Home_InProgressSince", Loc.Time(workout.StartedAt), Loc.Count(detail.Entries.Count, "Exercise"));
        }
        IsInProgress = _inProgress is not null;

        var format = Loc.Culture.DateTimeFormat;
        var firstDay = now.Date.AddDays(-(((int)now.DayOfWeek - (int)format.FirstDayOfWeek + 7) % 7));
        var days = await store.GetWorkoutDaysAsync(firstDay, firstDay.AddDays(6));
        Week = Enumerable.Range(0, 7)
            .Select(i => (Index: i, Day: firstDay.AddDays(i)))
            .Select(d => new WeekDayItem(
                d.Index,
                Loc.Capitalize(format.GetShortestDayName(d.Day.DayOfWeek))[..1],
                days.Contains(d.Day),
                d.Day == now.Date))
            .ToList();
        var recent = await store.GetRecentWorkoutsAsync(RecentCount);
        WeekText = Loc.Count(recent.Count(s => s.Workout.StartedAt >= firstDay), "Workout");

        Recent = recent.Select(s => new WorkoutItemViewModel(s, OpenWorkoutAsync)).ToList();
        HasRecent = Recent.Count > 0;
        IsEmpty = !HasRecent;
        IsLoaded = true;
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        // Resumes the workout in progress if there is one, even if this screen hasn't reloaded yet.
        _inProgress = await store.StartWorkoutAsync(DateTime.Now);
        Palette.Haptic();
        await Shell.Current.GoToAsync(Routes.Workout);
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => Shell.Current.GoToAsync(Routes.Settings);

    private Task OpenWorkoutAsync(int workoutId) => Shell.Current.GoToAsync($"{Routes.Summary}?id={workoutId}");
}

/// <summary>A day of this week: a filled circle when trained.</summary>
/// <param name="Index">Its column, Monday (or the culture's first day) being 0.</param>
public sealed record WeekDayItem(int Index, string Letter, bool IsDone, bool IsToday)
{
    public bool IsRest => !IsDone && !IsToday;

    public bool IsTodayToDo => IsToday && !IsDone;
}

/// <summary>A finished workout in the list of the last ones.</summary>
public sealed class WorkoutItemViewModel
{
    public WorkoutItemViewModel(WorkoutSummary summary, Func<int, Task> open)
    {
        var workout = summary.Workout;
        var culture = Loc.Culture;
        DayNumber = workout.StartedAt.Day.ToString(culture);
        Month = workout.StartedAt.ToString("MMM", culture).TrimEnd('.').ToUpper(culture);
        DayText = Loc.Day(workout.StartedAt);
        // Named after the muscles worked the most: "Chest · Arms".
        Title = string.Join(" · ", summary.Exercises
            .GroupBy(e => e.Exercise.Muscle)
            .OrderByDescending(g => g.Sum(e => e.Sets.Count))
            .Take(3)
            .Select(g => Loc.Muscle(g.Key)));
        Caption = string.Join(" · ", new[]
        {
            Loc.Duration(summary.Duration),
            Loc.Count(summary.Exercises.Count, "Exercise"),
            summary.Volume > 0 ? Loc.Volume(summary.Volume) : null,
        }.OfType<string>());
        HasRecords = summary.Records.Count > 0;
        RecordsText = summary.Records.Count.ToString(culture);
        Feeling = workout.Feeling is { } feeling ? SummaryViewModel.FeelingEmojis[feeling - 1] : string.Empty;
        OpenCommand = new AsyncRelayCommand(() => open(workout.Id));
    }

    public string DayNumber { get; }

    public string Month { get; }

    public string DayText { get; }

    public string Title { get; }

    public string Caption { get; }

    public bool HasRecords { get; }

    public string RecordsText { get; }

    public string Feeling { get; }

    public IAsyncRelayCommand OpenCommand { get; }
}
