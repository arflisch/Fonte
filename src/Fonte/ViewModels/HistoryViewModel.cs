using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>Every workout month by month: a calendar, the month's totals, the streak, and the list.</summary>
public sealed partial class HistoryViewModel(FonteStore store, AppSettings settings, ProService pro) : ReloadingViewModel
{
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private MonthReport? _report;
    private int _regularWeeks;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _monthTitle = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CalendarDayHeader> _dayHeaders = [];

    [ObservableProperty]
    private IReadOnlyList<CalendarDay> _days = [];

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private string _countText = "0";

    [ObservableProperty]
    private string _hoursText = "0";

    [ObservableProperty]
    private string _streakText = "0";

    [ObservableProperty]
    private string _volumeText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<WorkoutItemViewModel> _workouts = [];

    [ObservableProperty]
    private bool _hasWorkouts;

    protected override async Task LoadCoreAsync()
    {
        var culture = Loc.Culture;
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        var today = DateTime.Today;
        MonthTitle = Loc.Capitalize(_month.ToString("MMMM yyyy", culture));
        CanGoNext = _month < new DateTime(today.Year, today.Month, 1);

        DayHeaders = Enumerable.Range(0, 7)
            .Select(i => new CalendarDayHeader(i, Loc.Capitalize(culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)firstDay + i) % 7)))[..1]))
            .ToList();

        _report = await store.GetMonthAsync(_month.Year, _month.Month);
        var done = _report.Days;
        var offset = ((int)_month.DayOfWeek - (int)firstDay + 7) % 7;
        var length = DateTime.DaysInMonth(_month.Year, _month.Month);
        Days = Enumerable.Range(1, length)
            .Select(d =>
            {
                var date = new DateTime(_month.Year, _month.Month, d);
                var cell = offset + d - 1;
                return new CalendarDay(cell / 7, cell % 7, d.ToString(culture), done.Contains(date), date == today, date > today, () => OpenDayAsync(date));
            })
            .ToList();

        CountText = _report.Workouts.Count.ToString(culture);
        HoursText = _report.Workouts.Count == 0 ? "0" : Loc.Duration(_report.Duration);
        VolumeText = _report.Volume > 0 ? Loc.Format("History_Volume", Loc.Volume(_report.Volume)) : string.Empty;
        var dates = await store.GetWorkoutDatesAsync();
        _regularWeeks = Streaks.RegularWeeks(dates, settings.SessionsPerWeek, today, firstDay);
        StreakText = Loc.Format("History_Weeks", _regularWeeks);
        IsLocked = !pro.IsUnlocked;

        Workouts = _report.Workouts.Select(w => new WorkoutItemViewModel(w, OpenWorkoutAsync)).ToList();
        HasWorkouts = Workouts.Count > 0;
    }

    private async Task OpenDayAsync(DateTime day)
    {
        var workout = _report?.Workouts.Where(w => w.Workout.StartedAt.Date == day).MinBy(w => w.Workout.StartedAt);
        if (workout is not null)
            await OpenWorkoutAsync(workout.Workout.Id);
    }

    private Task OpenWorkoutAsync(int workoutId) => Shell.Current.GoToAsync($"{Routes.Summary}?id={workoutId}");

    [RelayCommand]
    private void PreviousMonth()
    {
        _month = _month.AddMonths(-1);
        Palette.Haptic();
        RequestReload();
    }

    [RelayCommand]
    private void NextMonth()
    {
        if (!CanGoNext)
            return;
        _month = _month.AddMonths(1);
        Palette.Haptic();
        RequestReload();
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    /// <summary>The month as an image, to send or post (Fonte Pro).</summary>
    [RelayCommand]
    private async Task ShareMonthAsync()
    {
        if (!pro.IsUnlocked)
        {
            await Shell.Current.GoToAsync(Routes.Pro);
            return;
        }
        if (_report is not { } report)
            return;
        try
        {
            var path = await MonthCard.RenderAsync(report, _regularWeeks);
            await PresentationGuard.WaitUntilSettledAsync();
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = MonthTitle,
                File = new ShareFile(path, "image/png"),
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Month card not shared: {ex}");
        }
    }
}

public sealed record CalendarDayHeader(int Column, string Letter);

/// <summary>A day of the month in the calendar: a filled circle when trained.</summary>
public sealed class CalendarDay(int row, int column, string number, bool isDone, bool isToday, bool isFuture, Func<Task> open)
{
    public int Row { get; } = row;

    public int Column { get; } = column;

    public string Number { get; } = number;

    public bool IsDone { get; } = isDone;

    public bool IsToday { get; } = isToday;

    public bool IsTodayToDo => IsToday && !IsDone;

    public double Opacity { get; } = isFuture ? 0.35 : 1;

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(open);
}
