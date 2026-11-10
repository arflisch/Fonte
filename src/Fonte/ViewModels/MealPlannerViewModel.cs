using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Nutrition;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>Sent when a food is chosen for the meal planner.</summary>
public sealed record PlanFoodPickedMessage(int FoodId);

/// <summary>
/// "With my ingredients" (Fonte Pro): the user picks the foods they want, Fonte works out how many grams of each
/// reach the meal's calories and protein, suggests how to cook it, and logs it.
/// </summary>
public sealed partial class MealPlannerViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private readonly FonteStore _store;
    private readonly AppSettings _settings;
    private readonly HealthService _health;
    private readonly List<Food> _foods = [];
    private DateTime _date = DateTime.Today;
    private Meal _meal = FoodEntryViewModel.MealAt(DateTime.Now);
    private MealPlan? _plan;
    private bool _isSaving;

    public MealPlannerViewModel(FonteStore store, AppSettings settings, HealthService health)
    {
        _store = store;
        _settings = settings;
        _health = health;
        WeakReferenceMessenger.Default.Register<MealPlannerViewModel, PlanFoodPickedMessage>(this, static (vm, m) => vm.OnPicked(m.FoodId));
    }

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public ObservableCollection<PlannedFoodItem> Items { get; } = [];

    [ObservableProperty]
    private string _kcalTarget = string.Empty;

    [ObservableProperty]
    private string _proteinTarget = string.Empty;

    [ObservableProperty]
    private bool _hasPlan;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _macrosText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<StepItem> _steps = [];

    [ObservableProperty]
    private string _addText = string.Empty;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("date", out var date)
            && DateTime.TryParseExact(date?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            _date = day;
        if (query.TryGetValue("meal", out var meal) && Enum.TryParse<Meal>(meal?.ToString(), out var m))
            _meal = m;
        AddText = Loc.Format("Recipes_AddTo", Loc.Meal(_meal));
        var target = await MealTargets.ForMealAsync(_store, _settings, _date, _meal);
        KcalTarget = Loc.Whole(target is { Kcal: > 0 } t ? t.Kcal : 600);
        ProteinTarget = Loc.Whole(target is { Protein: > 0 } tp ? tp.Protein : 35);
    }

    partial void OnKcalTargetChanged(string value) => Solve();

    partial void OnProteinTargetChanged(string value) => Solve();

    private void OnPicked(int foodId) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (_foods.Any(f => f.Id == foodId) || await _store.GetFoodAsync(foodId) is not { } food)
            return;
        _foods.Add(food);
        Solve();
    });

    private static double Number(string text) =>
        double.TryParse(text.Trim().Replace(",", ".").Replace(" ", string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? Math.Max(0, v) : 0;

    private void Solve()
    {
        Items.Clear();
        HasPlan = _foods.Count > 0;
        if (!HasPlan)
            return;
        var kcal = Number(KcalTarget);
        var protein = Number(ProteinTarget);
        var foods = _foods.Select(f =>
        {
            var (min, max) = PortionSolver.Bounds(f.Kcal, f.Protein, f.Fat);
            return new PlanFood(f.Id, Loc.FoodName(f), f.Kcal, f.Protein, f.Carbs, f.Fat, min, max);
        }).ToList();
        _plan = PortionSolver.Solve(foods, new Macros(kcal, protein, 0, 0));
        foreach (var (food, grams) in _plan.Portions)
            Items.Add(new PlannedFoodItem(food, grams, Remove));
        TotalText = Loc.Kcal(_plan.Totals.Kcal);
        MacrosText = Loc.Format("Nutrition_GoalMacros", Loc.Whole(_plan.Totals.Protein), Loc.Whole(_plan.Totals.Carbs), Loc.Whole(_plan.Totals.Fat));
        Steps = PreparationSteps(_plan).Select((s, i) => new StepItem(i + 1, s)).ToList();
    }

    /// <summary>A simple way to cook the meal, worded from what each food is.</summary>
    private static IEnumerable<string> PreparationSteps(MealPlan plan)
    {
        string Names(PortionSolver.Role role) => string.Join(", ", plan.Portions
            .Where(p => PortionSolver.RoleOf(p.Food.Kcal, p.Food.Protein, p.Food.Carbs, p.Food.Fat) == role)
            .Select(p => $"{p.Food.Name} ({Loc.Grams(p.Grams)})"));
        var starch = Names(PortionSolver.Role.Starch);
        var protein = Names(PortionSolver.Role.Protein);
        var vegetables = Names(PortionSolver.Role.Vegetable);
        var fat = Names(PortionSolver.Role.Fat);
        var other = Names(PortionSolver.Role.Other);
        if (starch.Length > 0)
            yield return Loc.Format("Planner_StepStarch", starch);
        if (protein.Length > 0)
            yield return fat.Length > 0 ? Loc.Format("Planner_StepProteinFat", protein, fat) : Loc.Format("Planner_StepProtein", protein);
        else if (fat.Length > 0)
            yield return Loc.Format("Planner_StepFat", fat);
        if (vegetables.Length > 0)
            yield return Loc.Format("Planner_StepVegetables", vegetables);
        if (other.Length > 0)
            yield return Loc.Format("Planner_StepOther", other);
        yield return Loc.Get("Planner_StepServe");
    }

    private void Remove(PlannedFoodItem item)
    {
        _foods.RemoveAll(f => f.Id == item.FoodId);
        Solve();
    }

    [RelayCommand]
    private Task AddFoodAsync() => Shell.Current.GoToAsync($"{Routes.FoodSearch}?mode=plan");

    [RelayCommand]
    private async Task AddAsync()
    {
        if (_plan is null || _isSaving)
            return;
        _isSaving = true;
        try
        {
            var entries = await _store.LogMealPlanAsync(_plan, _date, _meal, DateTime.Now);
            foreach (var entry in entries)
            {
                if (_foods.FirstOrDefault(f => f.Id == entry.FoodId) is { } food)
                    await _health.SaveFoodAsync(entry, Loc.FoodName(food));
            }
            Palette.Haptic();
            SuccessToast.Show(Loc.Format("FoodEntry_Added", Loc.Meal(_meal)));
            await Shell.Current.GoToAsync("../..");
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}

public sealed class PlannedFoodItem
{
    public PlannedFoodItem(PlanFood food, double grams, Action<PlannedFoodItem> remove)
    {
        FoodId = food.FoodId;
        Name = food.Name;
        Grams = Loc.Grams(grams);
        Detail = Loc.Format("Planner_FoodDetail", Loc.Kcal(food.Kcal * grams / 100), Loc.Whole(food.Protein * grams / 100));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public int FoodId { get; }

    public string Name { get; }

    public string Grams { get; }

    public string Detail { get; }

    public IRelayCommand RemoveCommand { get; }
}
