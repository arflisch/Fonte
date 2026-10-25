using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Nutrition;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>"Nutrition" tab (Fonte Pro): a day's meals against the calorie and macro targets, water, the last week.</summary>
public sealed partial class NutritionViewModel(
    FonteStore store, AppSettings settings, ProService pro, HealthService health, IDialogService dialogs) : ReloadingViewModel
{
    public const int GlassMl = 250;

    private DateTime _date = DateTime.Today;
    private NutritionDay? _day;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked))]
    private bool _isPro;

    public bool IsLocked => !IsPro;

    [ObservableProperty]
    private bool _hasProfile;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private bool _canGoNext;

    [ObservableProperty]
    private bool _isToday = true;

    [ObservableProperty]
    private string _remainingText = string.Empty;

    [ObservableProperty]
    private string _remainingLabel = string.Empty;

    [ObservableProperty]
    private bool _isOver;

    [ObservableProperty]
    private double _calorieProgress;

    [ObservableProperty]
    private string _goalText = "—";

    [ObservableProperty]
    private string _eatenText = "0";

    [ObservableProperty]
    private string _activeText = "0";

    [ObservableProperty]
    private bool _showActive;

    [ObservableProperty]
    private IReadOnlyList<MacroItem> _macros = [];

    [ObservableProperty]
    private IReadOnlyList<MealCardViewModel> _meals = [];

    [ObservableProperty]
    private string _waterText = string.Empty;

    [ObservableProperty]
    private double _waterProgress;

    [ObservableProperty]
    private IReadOnlyList<double> _week = [];

    [ObservableProperty]
    private IReadOnlyList<string> _weekLabels = [];

    [ObservableProperty]
    private double _weekTarget;

    [ObservableProperty]
    private string _weekText = string.Empty;

    [ObservableProperty]
    private bool _hasWeek;

    [ObservableProperty]
    private string _weekKcalText = "—";

    [ObservableProperty]
    private string _weekProteinText = "—";

    [ObservableProperty]
    private string _weekOnTargetText = "—";

    [ObservableProperty]
    private string _weekWeightText = "—";

    [ObservableProperty]
    private bool _showsAdaptive;

    [ObservableProperty]
    private string _adaptiveText = string.Empty;

    private bool _isTrainingDay;
    private bool _workedOut;

    [ObservableProperty]
    private bool _showsDayKind;

    [ObservableProperty]
    private string _dayKindText = string.Empty;

    [ObservableProperty]
    private bool _showsFasting;

    [ObservableProperty]
    private bool _isEatingWindow;

    [ObservableProperty]
    private string _fastingTitle = string.Empty;

    [ObservableProperty]
    private string _fastingDetail = string.Empty;

    [ObservableProperty]
    private double _fastingProgress;

    protected override async Task LoadCoreAsync()
    {
        IsPro = pro.IsUnlocked;
        if (!IsPro)
            return;

        var today = DateTime.Today;
        if (_date > today)
            _date = today;
        IsToday = _date == today;
        CanGoNext = _date < today;
        DateText = Loc.Day(_date);

        _day = await store.GetNutritionDayAsync(_date);
        var totals = _day.Totals;
        var profile = settings.NutritionProfile;
        HasProfile = profile is not null;
        var weights = await store.GetWeightsAsync();
        var weight = weights.LastOrDefault(w => w.Date <= _date)?.Kilograms ?? weights.LastOrDefault()?.Kilograms ?? 75;
        if (profile is not null)
            profile = await AdaptAsync(profile, weights);
        var baseTargets = profile is null ? null : Energy.Targets(profile, weight, _date);

        // Training days get more when the user asked for it; a workout makes it one, unless the user said otherwise.
        var workedOut = (await store.GetWorkoutDaysAsync(_date, _date)).Count > 0;
        var isTraining = settings.DayKinds.TryGetValue(_date, out var said) ? said : workedOut;
        _isTrainingDay = isTraining;
        _workedOut = workedOut;
        ShowsDayKind = profile is { TrainingDays: true } && baseTargets is not null;
        var targets = profile is null || baseTargets is null ? null : Energy.ForDay(profile, baseTargets, isTraining, settings.SessionsPerWeek);
        if (ShowsDayKind && targets is not null && baseTargets is not null)
        {
            var change = targets.Kcal - baseTargets.Kcal;
            DayKindText = isTraining
                ? Loc.Format("Nutrition_TrainingDay", $"+{Loc.Whole(change)}")
                : Loc.Format("Nutrition_RestDay", $"−{Loc.Whole(-change)}");
        }

        var active = settings.CountActiveEnergy ? await health.GetActiveEnergyAsync(_date) : 0;
        ShowActive = settings.CountActiveEnergy && health.IsActive;
        var budget = (targets?.Kcal ?? 0) + active;
        GoalText = targets is null ? "—" : Loc.Whole(targets.Kcal);
        EatenText = Loc.Whole(totals.Kcal);
        ActiveText = Loc.Whole(active);
        if (targets is null)
        {
            RemainingText = Loc.Whole(totals.Kcal);
            RemainingLabel = Loc.Get("Nutrition_Eaten");
            IsOver = false;
            CalorieProgress = 0;
        }
        else
        {
            var left = budget - totals.Kcal;
            IsOver = left < 0;
            RemainingText = Loc.Whole(Math.Abs(left));
            RemainingLabel = IsOver ? Loc.Get("Nutrition_Over") : Loc.Get("Nutrition_Remaining");
            CalorieProgress = budget > 0 ? Math.Min(1, totals.Kcal / budget) : 0;
        }

        Macros =
        [
            new MacroItem(Loc.Get("Nutrition_Protein"), totals.Protein, targets?.Protein, Color.FromArgb("#E24B4A")),
            new MacroItem(Loc.Get("Nutrition_Carbs"), totals.Carbs, targets?.Carbs, Color.FromArgb("#378ADD")),
            new MacroItem(Loc.Get("Nutrition_Fat"), totals.Fat, targets?.Fat, Color.FromArgb("#E8B22C")),
        ];
        Meals = _day.Meals.Select(m => new MealCardViewModel(m, AddFoodAsync, MealMenuAsync, OpenEntryAsync)).ToList();

        var goalWater = settings.WaterGoalMl;
        WaterText = Loc.Format("Nutrition_WaterValue", Loc.Number(_day.WaterMl / 1000.0), Loc.Number(goalWater / 1000.0));
        WaterProgress = Math.Min(1, (double)_day.WaterMl / goalWater);

        var week = await store.GetDailyIntakeAsync(_date.AddDays(-6), _date);
        Week = week.Select(d => d.Kcal).ToList();
        WeekTarget = baseTargets?.Kcal ?? 0;
        WeekLabels = week.Select(d => Loc.Capitalize(Loc.Culture.DateTimeFormat.GetShortestDayName(d.Date.DayOfWeek))[..1]).ToList();
        ShowWeekSummary(week, baseTargets, weights);
        UpdateFasting();
    }

    /// <summary>The week in four figures: calories and protein on the days logged, days on target, weight change.</summary>
    private void ShowWeekSummary(IReadOnlyList<DailyIntake> week, NutritionTargets? targets, IReadOnlyList<BodyWeight> weights)
    {
        var logged = week.Where(d => d.IsLogged).ToList();
        HasWeek = logged.Count > 0;
        WeekText = HasWeek ? Loc.Format("Nutrition_WeekLogged", logged.Count) : Loc.Get("Nutrition_WeekEmpty");
        WeekKcalText = HasWeek ? Loc.Whole(logged.Average(d => d.Kcal)) : "—";
        WeekProteinText = HasWeek ? Loc.Grams(logged.Average(d => d.Protein)) : "—";
        WeekOnTargetText = HasWeek && targets is not null
            ? $"{logged.Count(d => Math.Abs(d.Kcal - targets.Kcal) <= targets.Kcal * 0.1)}/{logged.Count}"
            : "—";
        var end = weights.LastOrDefault(w => w.Date <= _date);
        var start = weights.LastOrDefault(w => w.Date <= _date.AddDays(-7)) ?? weights.FirstOrDefault(w => w.Date >= _date.AddDays(-7));
        WeekWeightText = end is not null && start is not null && end != start
            ? BodyViewModel.Signed(Loc.ShownWeight(end.Kilograms - start.Kilograms), Loc.WeightUnit)
            : "—";
    }

    /// <summary>
    /// Once a week, measures maintenance from the last three weeks and keeps it in the profile, so the target
    /// follows what really happens. Says what is still missing until there is enough to measure.
    /// </summary>
    private async Task<NutritionProfile> AdaptAsync(NutritionProfile profile, IReadOnlyList<BodyWeight> weights)
    {
        ShowsAdaptive = profile.Adaptive;
        if (!profile.Adaptive)
            return profile;
        var today = DateTime.Today;
        if (profile.AdaptedOn is { } on && (today - on).TotalDays < 7 && profile.MeasuredMaintenance is { } kept)
        {
            AdaptiveText = Loc.Format("Nutrition_AdaptedOn", Loc.Kcal(kept), Loc.Date(on, "d MMMM"));
            return profile;
        }

        var days = await store.GetDailyIntakeAsync(today.AddDays(-Energy.AdaptiveWindowDays), today.AddDays(-1));
        var pairs = weights.Select(w => (w.Date, w.Kilograms)).ToList();
        if (Energy.MeasureMaintenance(days, pairs, today) is { } estimate)
        {
            profile = profile with { MeasuredMaintenance = estimate.Kcal, AdaptedOn = today };
            settings.NutritionProfile = profile;
            AdaptiveText = Loc.Format("Nutrition_AdaptedNow", Loc.Kcal(estimate.Kcal), estimate.LoggedDays,
                BodyViewModel.Signed(Loc.ShownWeight(estimate.KgPerWeek), Loc.WeightUnit));
            return profile;
        }

        var logged = days.Count(d => d.Kcal >= Energy.LoggedDayMinimum);
        var weighed = weights.Count(w => w.Date >= today.AddDays(-Energy.AdaptiveWindowDays));
        AdaptiveText = Loc.Format("Nutrition_AdaptiveWaiting",
            Math.Min(logged, Energy.AdaptiveLoggedDays), Energy.AdaptiveLoggedDays, Math.Min(weighed, 3));
        return profile;
    }

    /// <summary>Where the day is in the fasting plan: fasting since when, or eating until when. Refreshed every minute.</summary>
    public void UpdateFasting()
    {
        var hours = settings.FastingHours;
        ShowsFasting = IsPro && hours > 0 && IsToday;
        if (!ShowsFasting)
            return;
        var now = DateTime.Now;
        var opens = DateTime.Today.AddHours(settings.EatingStartHour);
        var window = TimeSpan.FromHours(24 - hours);
        // The window that started last: today's, or yesterday's when it hasn't opened yet.
        if (now < opens)
            opens = opens.AddDays(-1);
        var closes = opens + window;
        IsEatingWindow = now < closes;
        if (IsEatingWindow)
        {
            FastingTitle = Loc.Get("Fasting_Eating");
            FastingDetail = Loc.Format("Fasting_ClosesAt", Loc.Time(closes), Loc.Duration(closes - now));
            FastingProgress = (now - opens) / window;
        }
        else
        {
            var fasted = now - closes;
            FastingTitle = Loc.Format("Fasting_Fasting", Loc.Duration(fasted), hours);
            FastingDetail = Loc.Format("Fasting_OpensAt", Loc.Time(opens.AddDays(1)));
            FastingProgress = Math.Min(1, fasted.TotalHours / hours);
        }
    }

    [RelayCommand]
    private void ToggleDayKind()
    {
        var training = !_isTrainingDay;
        // Back to what the workouts say: nothing to remember.
        settings.SetDayKind(_date, training == _workedOut ? null : training);
        Palette.Haptic();
    }

    internal static string Iso(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task AddFoodAsync(Meal meal) => Shell.Current.GoToAsync($"{Routes.FoodSearch}?date={Iso(_date)}&meal={meal}");

    private Task OpenEntryAsync(int entryId) => Shell.Current.GoToAsync($"{Routes.FoodEntry}?entry={entryId}");

    private async Task MealMenuAsync(Meal meal)
    {
        var copy = Loc.Get("Nutrition_CopyYesterday");
        var clear = Loc.Get("Nutrition_ClearMeal");
        var save = Loc.Get("Nutrition_SaveTemplate");
        var hasFoods = _day?.Meals.Single(m => m.Meal == meal).Foods.Count > 0;
        var choice = await dialogs.ChooseAsync(Loc.Meal(meal), hasFoods ? clear : null, hasFoods ? [copy, save] : [copy]);
        if (choice == save)
        {
            var name = await dialogs.PromptAsync(save, Loc.Get("Nutrition_TemplateName"), Loc.Meal(meal), 60);
            if (string.IsNullOrWhiteSpace(name))
                return;
            await store.SaveMealTemplateAsync(name, _date, meal, DateTime.Now);
            Palette.Haptic();
            SuccessToast.Show(Loc.Get("Nutrition_TemplateSaved"));
        }
        else if (choice == copy)
        {
            var copies = await store.CopyMealAsync(_date.AddDays(-1), _date, meal, DateTime.Now);
            if (copies.Count == 0)
            {
                await dialogs.AlertAsync(Loc.Meal(meal), Loc.Get("Nutrition_NothingYesterday"));
                return;
            }
            Palette.Haptic();
            foreach (var entry in copies)
            {
                if (await store.GetFoodAsync(entry.FoodId) is { } food)
                    await health.SaveFoodAsync(entry, Loc.FoodName(food));
            }
        }
        else if (choice == clear)
        {
            var removed = await store.ClearMealAsync(_date, meal);
            await health.DeleteFoodsAsync(removed);
            Palette.Haptic();
        }
    }

    [RelayCommand]
    private void PreviousDay()
    {
        _date = _date.AddDays(-1);
        Palette.Haptic();
        RequestReload();
    }

    [RelayCommand]
    private void NextDay()
    {
        if (_date >= DateTime.Today)
            return;
        _date = _date.AddDays(1);
        Palette.Haptic();
        RequestReload();
    }

    [RelayCommand]
    private void GoToToday()
    {
        _date = DateTime.Today;
        RequestReload();
    }

    [RelayCommand]
    private async Task AddWaterAsync()
    {
        var water = await store.AddWaterAsync(_date, GlassMl, DateTime.Now);
        Palette.Haptic();
        await health.SaveWaterAsync(water);
    }

    [RelayCommand]
    private async Task RemoveWaterAsync()
    {
        if (await store.RemoveLastWaterAsync(_date) is { } removed)
        {
            Palette.Haptic();
            await health.DeleteWaterAsync(removed);
        }
    }

    [RelayCommand]
    private Task OpenGoalAsync() => Shell.Current.GoToAsync(Routes.NutritionGoal);

    [RelayCommand]
    private Task OpenProAsync() => Shell.Current.GoToAsync(Routes.Pro);
}

/// <summary>A macronutrient eaten against its target.</summary>
public sealed class MacroItem(string name, double eaten, double? goal, Color color)
{
    public string Name { get; } = name;

    public string ValueText { get; } = goal is { } g ? $"{Loc.Whole(eaten)} / {Loc.Whole(g)} g" : Loc.Grams(eaten);

    public double Progress { get; } = goal is > 0 ? Math.Min(1, eaten / goal.Value) : 0;

    public Color Color { get; } = color;

    public Color TrackColor { get; } = Palette.Soft(color);
}

/// <summary>A meal of the day with what was eaten.</summary>
public sealed class MealCardViewModel
{
    public MealCardViewModel(MealLog log, Func<Meal, Task> add, Func<Meal, Task> menu, Func<int, Task> open)
    {
        Name = Loc.Meal(log.Meal);
        Emoji = log.Meal switch
        {
            Meal.Breakfast => "🥐",
            Meal.Lunch => "🥗",
            Meal.Dinner => "🍽️",
            _ => "🍎",
        };
        var totals = log.Totals;
        KcalText = log.Foods.Count == 0 ? string.Empty : Loc.Kcal(totals.Kcal);
        MacrosText = log.Foods.Count == 0
            ? Loc.Get("Nutrition_MealEmpty")
            : Loc.Format("Nutrition_MacrosShort", Loc.Whole(totals.Protein), Loc.Whole(totals.Carbs), Loc.Whole(totals.Fat));
        Foods = log.Foods.Select(f => new LoggedFoodItem(f, open)).ToList();
        HasFoods = Foods.Count > 0;
        AddCommand = new AsyncRelayCommand(() => add(log.Meal));
        MenuCommand = new AsyncRelayCommand(() => menu(log.Meal));
    }

    public string Name { get; }

    public string Emoji { get; }

    public string KcalText { get; }

    public string MacrosText { get; }

    public IReadOnlyList<LoggedFoodItem> Foods { get; }

    public bool HasFoods { get; }

    public IAsyncRelayCommand AddCommand { get; }

    public IAsyncRelayCommand MenuCommand { get; }
}

public sealed class LoggedFoodItem(LoggedFood logged, Func<int, Task> open)
{
    public string Name { get; } = Loc.FoodName(logged.Food);

    public string Detail { get; } = logged.Food.Brand is { } brand
        ? $"{brand} · {Loc.Grams(logged.Entry.Grams)}"
        : Loc.Grams(logged.Entry.Grams);

    public string KcalText { get; } = Loc.Whole(logged.Entry.Kcal);

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => open(logged.Entry.Id));
}
