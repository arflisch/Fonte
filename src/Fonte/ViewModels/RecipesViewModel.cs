using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Nutrition;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>What a meal should bring: its share of the day's target, no more than what is left (Fonte Pro).</summary>
public static class MealTargets
{
    public static async Task<Macros?> ForMealAsync(FonteStore store, AppSettings settings, DateTime date, Meal meal)
    {
        if (settings.NutritionProfile is not { } profile)
            return null;
        var weights = await store.GetWeightsAsync();
        var weight = weights.LastOrDefault(w => w.Date <= date)?.Kilograms ?? weights.LastOrDefault()?.Kilograms ?? 75;
        var targets = Energy.Targets(profile, weight, date);
        var workedOut = (await store.GetWorkoutDaysAsync(date, date)).Count > 0;
        var training = settings.DayKinds.TryGetValue(date.Date, out var said) ? said : workedOut;
        targets = Energy.ForDay(profile, targets, training, settings.SessionsPerWeek);

        var eaten = (await store.GetNutritionDayAsync(date)).Totals;
        var share = RecipeCatalog.MealShare(meal);
        double Part(double day, double done) => Math.Max(0, Math.Min(day * share, day - done));
        var kcal = Part(targets.Kcal, eaten.Kcal);
        var protein = Part(targets.Protein, eaten.Protein);
        return new Macros(kcal, Math.Min(protein, kcal / 4), Part(targets.Carbs, eaten.Carbs), Part(targets.Fat, eaten.Fat));
    }
}

/// <summary>
/// The built-in recipes for a meal (Fonte Pro), the best fit first, each with the portions that reach the meal's
/// calories; filters by meal and by kind; and the way to the meal planner from one's own ingredients.
/// </summary>
public sealed partial class RecipesViewModel(FonteStore store, AppSettings settings) : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private DateTime _date = DateTime.Today;
    private Meal _meal = FoodEntryViewModel.MealAt(DateTime.Now);
    private Macros? _target;
    private string? _tag;

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _meals = [];

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _tags = [];

    [ObservableProperty]
    private IReadOnlyList<RecipeItemViewModel> _recipes = [];

    [ObservableProperty]
    private string _targetText = string.Empty;

    [ObservableProperty]
    private bool _hasTarget;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("date", out var date)
            && DateTime.TryParseExact(date?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            _date = day;
        if (query.TryGetValue("meal", out var meal) && Enum.TryParse<Meal>(meal?.ToString(), out var m))
            _meal = m;
        Meals = Enum.GetValues<Meal>()
            .Select((x, i) => new SelectableOption(x.ToString(), i, 4, o => { _meal = Enum.Parse<Meal>(o.Value); _ = RefreshAsync(); }) { Label = Loc.Meal(x) })
            .ToList();
        Tags = new[] { "protein", "vegetarian", "quick" }
            .Select((x, i) => new SelectableOption(x, i, 3, SelectTag) { Label = Loc.Get($"RecipeTag_{x}") })
            .ToList();
        await RefreshAsync();
    }

    private void SelectTag(SelectableOption option)
    {
        _tag = _tag == option.Value ? null : option.Value;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            foreach (var o in Meals)
                o.IsSelected = o.Value == _meal.ToString();
            foreach (var o in Tags)
                o.IsSelected = o.Value == _tag;
            _target = await MealTargets.ForMealAsync(store, settings, _date, _meal);
            HasTarget = _target is { Kcal: > 0 };
            TargetText = HasTarget
                ? Loc.Format("Recipes_Target", Loc.Meal(_meal), Loc.Kcal(_target!.Kcal), Loc.Whole(_target.Protein))
                : Loc.Get("Recipes_NoTarget");
            var pool = RecipeCatalog.All.Where(r => _tag is null || r.Tags.Contains(_tag));
            IEnumerable<RecipeSuggestion> list = HasTarget
                ? RecipeCatalog.Suggest(_target!, _meal, pool)
                : pool.Where(r => r.Meal == _meal || (_meal is Meal.Lunch or Meal.Dinner && r.Meal is Meal.Lunch or Meal.Dinner))
                    .Select(r => new RecipeSuggestion(r, 1, r.PortionMacros));
            Recipes = list.Select(s => new RecipeItemViewModel(s, OpenAsync)).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recipes failed: {ex}");
        }
    }

    private Task OpenAsync(RecipeSuggestion suggestion) => Shell.Current.GoToAsync(
        $"{Routes.RecipeDetail}?key={suggestion.Recipe.Key}&portions={suggestion.Portions.ToString(CultureInfo.InvariantCulture)}&date={NutritionViewModel.Iso(_date)}&meal={_meal}");

    [RelayCommand]
    private Task OpenPlannerAsync() => Shell.Current.GoToAsync($"{Routes.MealPlanner}?date={NutritionViewModel.Iso(_date)}&meal={_meal}");

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}

public sealed class RecipeItemViewModel(RecipeSuggestion suggestion, Func<RecipeSuggestion, Task> open)
{
    public string Name { get; } = suggestion.Recipe.Name(Loc.LanguageCode);

    public string Detail { get; } = Loc.Format("Recipes_Portions", Loc.Number(suggestion.Portions), Loc.Kcal(suggestion.Macros.Kcal),
        Loc.Whole(suggestion.Macros.Protein));

    public string Minutes { get; } = Loc.Format("Recipes_Minutes", suggestion.Recipe.Minutes);

    public string Emoji { get; } = suggestion.Recipe.Meal switch
    {
        Meal.Breakfast => "🥣",
        Meal.Snack => "🍎",
        _ => suggestion.Recipe.Tags.Contains("vegetarian") ? "🥗" : "🍲",
    };

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => open(suggestion));
}
