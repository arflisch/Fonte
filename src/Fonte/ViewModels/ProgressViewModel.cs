using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>"Progress" tab: volume week after week, sets per muscle group, muscles left aside, this month's records.</summary>
public sealed partial class ProgressViewModel : ReloadingViewModel
{
    /// <summary>Sets per muscle group and per week usually advised to build muscle.</summary>
    public const int WeeklySetTarget = 10;

    private static readonly int[] Periods = [4, 12];

    private readonly FonteStore _store;
    private readonly ProService _pro;
    private ProgressReport? _report;
    private int _weeks = Periods[0];

    public ProgressViewModel(FonteStore store, ProService pro)
    {
        _store = store;
        _pro = pro;
        PeriodChips =
        [
            new SelectableOption("4", 0, 2, SelectPeriod) { Label = Loc.Get("Progress_Month"), IsSelected = true },
            new SelectableOption("12", 1, 2, SelectPeriod) { Label = Loc.Get("Progress_Quarter") },
        ];
    }

    public IReadOnlyList<SelectableOption> PeriodChips { get; }

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Three months and the muscle groups come with Fonte Pro.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    private bool _isPro;

    public bool IsLocked => !IsPro;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private IReadOnlyList<double> _volumes = [];

    [ObservableProperty]
    private IReadOnlyList<string> _weekLabels = [];

    [ObservableProperty]
    private string _averageText = string.Empty;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<MuscleBarItem> _muscles = [];

    [ObservableProperty]
    private string _alertText = string.Empty;

    [ObservableProperty]
    private bool _hasAlert;

    [ObservableProperty]
    private IReadOnlyList<MonthRecordItem> _records = [];

    [ObservableProperty]
    private bool _hasRecords;

    protected override async Task LoadCoreAsync()
    {
        IsPro = _pro.IsUnlocked;
        if (!IsPro && _weeks != Periods[0])
            SelectPeriod(PeriodChips[0]);
        _report = await _store.GetProgressAsync(DateTime.Today, Loc.Culture.DateTimeFormat.FirstDayOfWeek, Periods[^1]);
        var everDone = (await _store.GetWorkoutDatesAsync()).Count > 0;
        IsEmpty = !everDone;
        HasData = everDone;
        ShowPeriod();

        var sets = _report.MuscleSets;
        Muscles = Enum.GetValues<MuscleGroup>()
            .Where(m => m != MuscleGroup.Cardio)
            .Select(m => new MuscleBarItem(m, sets.GetValueOrDefault(m)))
            .ToList();

        var trainedThisWeek = sets.Values.Sum() > 0;
        if (_report.Neglected.Count > 0)
            AlertText = Loc.Format("Progress_Neglected", Join(_report.Neglected));
        else if (trainedThisWeek && Muscles.Where(m => m.Count < WeeklySetTarget).Select(m => m.Muscle).ToList() is { Count: > 0 } low)
            AlertText = Loc.Format("Progress_BelowTarget", Join(low), WeeklySetTarget);
        else
            AlertText = string.Empty;
        HasAlert = AlertText.Length > 0;

        Records = _report.MonthRecords
            .Select(r => new MonthRecordItem(Loc.ExerciseName(r.Record.Exercise), Loc.Set(r.Record.Exercise, r.Record.Set), Loc.Day(r.Date)))
            .ToList();
        HasRecords = Records.Count > 0;
    }

    private static string Join(IEnumerable<MuscleGroup> muscles) => string.Join(", ", muscles.Select(Loc.Muscle));

    private async void SelectPeriod(SelectableOption chip)
    {
        if (chip != PeriodChips[0] && !_pro.IsUnlocked)
        {
            await Shell.Current.GoToAsync(Routes.Pro);
            return;
        }
        foreach (var option in PeriodChips)
            option.IsSelected = option == chip;
        _weeks = int.Parse(chip.Value, Loc.Culture);
        Palette.Haptic();
        ShowPeriod();
    }

    private void ShowPeriod()
    {
        if (_report is null)
            return;
        var weeks = _report.Weeks.TakeLast(_weeks).ToList();
        Volumes = weeks.Select(w => Math.Round(w.Volume / 1000, 2)).ToList();
        // Twelve labels don't fit: one week out of two then.
        var every = weeks.Count > 6 ? 2 : 1;
        WeekLabels = weeks
            .Select((w, i) => (weeks.Count - 1 - i) % every == 0 ? w.Start.ToString("d/M", Loc.Culture) : string.Empty)
            .ToList();
        var active = weeks.Where(w => w.Workouts > 0).ToList();
        AverageText = active.Count > 0 ? Loc.Format("Progress_Average", Tonnes(active.Average(w => w.Volume))) : string.Empty;
        PeriodText = Loc.Format("Progress_PeriodTotals",
            Loc.Count(weeks.Sum(w => w.Workouts), "Workout"),
            Loc.Count(weeks.Sum(w => w.Sets), "Set"),
            Tonnes(weeks.Sum(w => w.Volume)));
    }

    [RelayCommand]
    private Task OpenProAsync() => Shell.Current.GoToAsync(Routes.Pro);

    private static string Tonnes(double kilograms) => $"{Loc.Number(Math.Round(kilograms / 1000, 1))} t";
}

/// <summary>Sets done for a muscle group over the last seven days, against the weekly target.</summary>
public sealed class MuscleBarItem(MuscleGroup muscle, int count)
{
    public MuscleGroup Muscle { get; } = muscle;

    public string Name { get; } = Loc.Muscle(muscle);

    public int Count { get; } = count;

    public string CountText { get; } = count.ToString(Loc.Culture);

    public double Progress { get; } = Math.Min(1, (double)count / ProgressViewModel.WeeklySetTarget);

    public Color Color { get; } = Palette.Muscle(muscle);

    public Color TrackColor { get; } = Palette.Soft(Palette.Muscle(muscle));
}

public sealed record MonthRecordItem(string Name, string SetText, string DayText);
