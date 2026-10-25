using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Nutrition;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>
/// Sheet to set up the daily targets: who the user is, how active, and whether to lose, keep or gain weight.
/// The calories and macronutrients are computed live (Mifflin-St Jeor) as the answers change.
/// </summary>
public sealed partial class NutritionGoalViewModel : ObservableObject, ISheetViewModel
{
    private static readonly double[] LossRates = [0.25, 0.5, 0.75, 1.0];
    private static readonly double[] GainRates = [0.25, 0.5];
    private static readonly double[] ProteinRates = [1.2, 1.6, 2.0];
    private static readonly int[] WaterGoals = [1500, 2000, 2500, 3000];
    private static readonly int[] FastingPlans = [0, 12, 14, 16, 18, 20];
    private static readonly int[] EatingStarts = [8, 10, 12, 14];

    private readonly FonteStore _store;
    private readonly AppSettings _settings;
    private readonly NutritionReminders _reminders;
    private int _fasting;
    private int _eatingStart;
    private double? _storedWeight;
    private Sex _sex = Sex.Female;
    private ActivityLevel _activity = ActivityLevel.Light;
    private WeightGoal _goal = WeightGoal.Lose;
    private double _rate = 0.5;
    private double _protein = 1.6;
    private int _water;
    private bool _isSaving;

    public NutritionGoalViewModel(FonteStore store, AppSettings settings, NutritionReminders reminders)
    {
        _store = store;
        _settings = settings;
        _reminders = reminders;
        _fasting = settings.FastingHours;
        _eatingStart = settings.EatingStartHour;
        FastingOptions = FastingPlans
            .Select((h, i) => new SelectableOption(h.ToString(CultureInfo.InvariantCulture), i, int.MaxValue, o => { _fasting = int.Parse(o.Value, CultureInfo.InvariantCulture); Refresh(); })
            {
                Label = h == 0 ? Loc.Get("Fasting_Off") : $"{h}:{24 - h}",
            })
            .ToList();
        EatingStartOptions = EatingStarts
            .Select((h, i) => new SelectableOption(h.ToString(CultureInfo.InvariantCulture), i, int.MaxValue, o => { _eatingStart = int.Parse(o.Value, CultureInfo.InvariantCulture); Refresh(); })
            {
                Label = Loc.Time(DateTime.Today.AddHours(h)),
            })
            .ToList();
        MealReminders = settings.MealReminders;
        WaterReminders = settings.WaterReminders;
        FastingReminders = settings.FastingReminders;
        TrainingDaysHint = Loc.Format("Nutrition_TrainingDaysHint", settings.SessionsPerWeek);
        Sexes = Options([Sex.Female, Sex.Male], 2, s => Loc.Get($"Sex_{s}"), o => { _sex = Enum.Parse<Sex>(o.Value); Refresh(); });
        Activities = Options(Enum.GetValues<ActivityLevel>(), 1, a => Loc.Get($"Activity_{a}"), o => { _activity = Enum.Parse<ActivityLevel>(o.Value); Refresh(); });
        Goals = Options(Enum.GetValues<WeightGoal>(), 3, g => Loc.Get($"WeightGoal_{g}"), o => { _goal = Enum.Parse<WeightGoal>(o.Value); FitRate(); Refresh(); });
        Proteins = ProteinRates
            .Select((p, i) => new SelectableOption(Invariant(p), i, 3, o => { _protein = Parse(o.Value); Refresh(); })
            {
                Label = Loc.Format("Nutrition_ProteinRate", Loc.Number(p)),
            })
            .ToList();
        Waters = WaterGoals
            .Select((w, i) => new SelectableOption(w.ToString(CultureInfo.InvariantCulture), i, 4, o => { _water = int.Parse(o.Value, CultureInfo.InvariantCulture); Refresh(); })
            {
                Label = $"{Loc.Number(w / 1000.0)} L",
            })
            .ToList();
        ShowsHealth = HealthService.IsSupported;

        var profile = settings.NutritionProfile;
        if (profile is not null)
        {
            _sex = profile.Sex;
            _activity = profile.Activity;
            _goal = profile.Goal;
            _rate = profile.WeeklyChangeKg;
            _protein = profile.ProteinPerKg;
            BirthYear = profile.BirthYear.ToString(CultureInfo.InvariantCulture);
            Height = Loc.Number(profile.HeightCm);
        }
        _water = settings.WaterGoalMl;
        Adaptive = profile?.Adaptive ?? true;
        TrainingDays = profile?.TrainingDays ?? false;
        AdaptiveHint = profile is { MeasuredMaintenance: { } measured, AdaptedOn: { } on }
            ? Loc.Format("Nutrition_AdaptiveMeasured", Loc.Kcal(measured), Loc.Date(on, "d MMMM"))
            : Loc.Get("Nutrition_AdaptiveHint");
        CountActive = settings.CountActiveEnergy;
        FitRate();
        _ = LoadWeightAsync();
    }

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public IReadOnlyList<SelectableOption> Sexes { get; }

    public IReadOnlyList<SelectableOption> Activities { get; }

    public IReadOnlyList<SelectableOption> Goals { get; }

    public IReadOnlyList<SelectableOption> Proteins { get; }

    public IReadOnlyList<SelectableOption> Waters { get; }

    public bool ShowsHealth { get; }

    public IReadOnlyList<SelectableOption> FastingOptions { get; }

    public IReadOnlyList<SelectableOption> EatingStartOptions { get; }

    public string TrainingDaysHint { get; }

    [ObservableProperty]
    private string _adaptiveHint = string.Empty;

    [ObservableProperty]
    private bool _adaptive;

    [ObservableProperty]
    private bool _trainingDays;

    [ObservableProperty]
    private bool _isFasting;

    [ObservableProperty]
    private string _fastingText = string.Empty;

    [ObservableProperty]
    private bool _mealReminders;

    [ObservableProperty]
    private bool _waterReminders;

    [ObservableProperty]
    private bool _fastingReminders;

    public string WeightLabel { get; } = Loc.Format("Nutrition_Weight", Loc.WeightUnit);

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _rates = [];

    [ObservableProperty]
    private bool _hasRate;

    [ObservableProperty]
    private string _birthYear = string.Empty;

    [ObservableProperty]
    private string _height = string.Empty;

    [ObservableProperty]
    private string _weight = string.Empty;

    [ObservableProperty]
    private bool _countActive;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _kcalText = string.Empty;

    [ObservableProperty]
    private string _detailText = string.Empty;

    [ObservableProperty]
    private string _macrosText = string.Empty;

    [ObservableProperty]
    private string _goalDateText = string.Empty;

    [ObservableProperty]
    private bool _hasGoalDate;

    [ObservableProperty]
    private bool _isFloored;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    partial void OnBirthYearChanged(string value) => Refresh();

    partial void OnHeightChanged(string value) => Refresh();

    partial void OnWeightChanged(string value) => Refresh();

    partial void OnAdaptiveChanged(bool value) => Refresh();

    partial void OnTrainingDaysChanged(bool value) => Refresh();

    private async Task LoadWeightAsync()
    {
        var weights = await _store.GetWeightsAsync();
        _storedWeight = weights.LastOrDefault()?.Kilograms;
        if (_storedWeight is { } kg)
            Weight = Loc.WeightNumber(kg);
        Refresh();
    }

    /// <summary>The answers, or null while one of them is missing or out of range.</summary>
    private (NutritionProfile Profile, double Weight)? Read()
    {
        if (!int.TryParse(BirthYear.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            || year < DateTime.Today.Year - 100 || year > DateTime.Today.Year - 14)
            return null;
        var kilograms = TryNumber(Weight) is { } typed ? Loc.TypedWeight(typed) : 0;
        if (TryNumber(Height) is not (>= 120 and <= 230) || kilograms is not (>= 30 and <= 300))
            return null;
        var previous = _settings.NutritionProfile;
        var profile = new NutritionProfile(_sex, year, TryNumber(Height)!.Value, _activity, _goal,
            _goal == WeightGoal.Maintain ? 0 : _rate, _protein,
            Adaptive, previous?.MeasuredMaintenance, previous?.AdaptedOn, TrainingDays);
        return (profile, kilograms);
    }

    private void Refresh()
    {
        HasError = false;
        foreach (var option in Sexes)
            option.IsSelected = option.Value == _sex.ToString();
        foreach (var option in Activities)
            option.IsSelected = option.Value == _activity.ToString();
        foreach (var option in Goals)
            option.IsSelected = option.Value == _goal.ToString();
        foreach (var option in Rates)
            option.IsSelected = Parse(option.Value) == _rate;
        foreach (var option in Proteins)
            option.IsSelected = Parse(option.Value) == _protein;
        foreach (var option in Waters)
            option.IsSelected = option.Value == _water.ToString(CultureInfo.InvariantCulture);
        foreach (var option in FastingOptions)
            option.IsSelected = option.Value == _fasting.ToString(CultureInfo.InvariantCulture);
        foreach (var option in EatingStartOptions)
            option.IsSelected = option.Value == _eatingStart.ToString(CultureInfo.InvariantCulture);
        IsFasting = _fasting > 0;
        FastingText = IsFasting
            ? Loc.Format("Fasting_Window", Loc.Time(DateTime.Today.AddHours(_eatingStart)), Loc.Time(DateTime.Today.AddHours(_eatingStart + 24 - _fasting)))
            : string.Empty;

        if (Read() is not var (profile, weight))
        {
            HasResult = HasGoalDate = IsFloored = false;
            return;
        }
        var targets = Energy.Targets(profile, weight, DateTime.Today);
        HasResult = true;
        KcalText = Loc.Kcal(targets.Kcal);
        DetailText = targets.IsMeasured
            ? Loc.Format("Nutrition_GoalDetailMeasured", Loc.Kcal(targets.Bmr), Loc.Kcal(targets.Maintenance))
            : Loc.Format("Nutrition_GoalDetail", Loc.Kcal(targets.Bmr), Loc.Kcal(targets.Maintenance));
        MacrosText = Loc.Format("Nutrition_GoalMacros", Loc.Number(targets.Protein), Loc.Number(targets.Carbs), Loc.Number(targets.Fat));
        IsFloored = targets.IsFloored;

        // When the body goal points the same way, say when it should be reached.
        var goalKg = _settings.GoalWeight;
        var heads = goalKg > 0 && (_goal == WeightGoal.Lose ? goalKg < weight : _goal == WeightGoal.Gain && goalKg > weight);
        var weekly = targets.IsFloored ? Math.Abs(targets.Balance) * 7 / Energy.KcalPerKg : profile.WeeklyChangeKg;
        HasGoalDate = heads && Energy.GoalDate(weight, goalKg, weekly, DateTime.Today) is not null;
        if (HasGoalDate)
            GoalDateText = Loc.Format("Nutrition_GoalDate", Loc.Weight(goalKg),
                Loc.Date(Energy.GoalDate(weight, goalKg, weekly, DateTime.Today)!.Value, "d MMMM yyyy"));
    }

    /// <summary>Shows the paces of the chosen direction and keeps the chosen one when it still applies.</summary>
    private void FitRate()
    {
        var rates = _goal switch
        {
            WeightGoal.Lose => LossRates,
            WeightGoal.Gain => GainRates,
            _ => [],
        };
        if (rates.Length > 0 && !rates.Contains(_rate))
            _rate = 0.5;
        Rates = rates
            .Select((r, i) => new SelectableOption(Invariant(r), i, rates.Length, o => { _rate = Parse(o.Value); Refresh(); })
            {
                Label = Loc.Format("Nutrition_PerWeek", Loc.Weight(r)),
            })
            .ToList();
        HasRate = rates.Length > 0;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_isSaving)
            return;
        if (Read() is not var (profile, weight))
        {
            ErrorMessage = Loc.Get("Nutrition_GoalIncomplete");
            HasError = true;
            return;
        }
        _isSaving = true;
        try
        {
            // A new weight typed here is a weighing like any other.
            if (_storedWeight is not { } stored || Math.Abs(stored - weight) >= 0.05)
                await _store.SaveWeightAsync(DateTime.Today, weight);
            _settings.WaterGoalMl = _water;
            _settings.CountActiveEnergy = CountActive;
            _settings.FastingHours = _fasting;
            _settings.EatingStartHour = _eatingStart;
            _settings.MealReminders = MealReminders;
            _settings.WaterReminders = WaterReminders;
            _settings.FastingReminders = FastingReminders && _fasting > 0;
            _settings.NutritionProfile = profile;
            await _reminders.UpdateAsync();
            Palette.Haptic();
            SuccessToast.Show(Loc.Get("Nutrition_GoalSaved"));
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private static double? TryNumber(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;

    private static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    private static IReadOnlyList<SelectableOption> Options<T>(
        IEnumerable<T> values, int columns, Func<T, string> label, Action<SelectableOption> select)
        where T : struct, Enum =>
        values.Select((v, i) => new SelectableOption(v.ToString(), i, columns, select) { Label = label(v) }).ToList();
}
