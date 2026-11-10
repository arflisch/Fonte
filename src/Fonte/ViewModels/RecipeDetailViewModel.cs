using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Nutrition;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>A built-in recipe: its ingredients for the portions chosen, what they bring, the steps, and logging it.</summary>
public sealed partial class RecipeDetailViewModel(FonteStore store, HealthService health) : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private CatalogRecipe? _recipe;
    private DateTime _date = DateTime.Today;
    private Meal _meal = FoodEntryViewModel.MealAt(DateTime.Now);
    private bool _isSaving;

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _info = string.Empty;

    [ObservableProperty]
    private double _portions = 1;

    [ObservableProperty]
    private string _portionsText = string.Empty;

    [ObservableProperty]
    private string _kcalText = string.Empty;

    [ObservableProperty]
    private string _proteinText = string.Empty;

    [ObservableProperty]
    private string _carbsText = string.Empty;

    [ObservableProperty]
    private string _fatText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<IngredientLineItem> _ingredients = [];

    [ObservableProperty]
    private IReadOnlyList<StepItem> _steps = [];

    [ObservableProperty]
    private string _addText = string.Empty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("date", out var date)
            && DateTime.TryParseExact(date?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            _date = day;
        if (query.TryGetValue("meal", out var meal) && Enum.TryParse<Meal>(meal?.ToString(), out var m))
            _meal = m;
        if (!query.TryGetValue("key", out var key) || RecipeCatalog.Find(key?.ToString() ?? string.Empty) is not { } recipe)
            return;
        _recipe = recipe;
        if (query.TryGetValue("portions", out var p) && double.TryParse(p?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var portions))
            Portions = Math.Clamp(portions, 0.25, 4);
        Name = recipe.Name(Loc.LanguageCode);
        Info = Loc.Format("Recipes_Info", Loc.Format("Recipes_Minutes", recipe.Minutes), recipe.Servings);
        Steps = recipe.StepsIn(Loc.LanguageCode).Select((s, i) => new StepItem(i + 1, s)).ToList();
        AddText = Loc.Format("Recipes_AddTo", Loc.Meal(_meal));
        Refresh();
    }

    partial void OnPortionsChanged(double value) => Refresh();

    private void Refresh()
    {
        if (_recipe is null)
            return;
        var factor = Portions / _recipe.Servings;
        PortionsText = Loc.Format("Recipes_PortionCount", Loc.Number(Portions));
        var macros = RecipeCatalog.Times(_recipe.PortionMacros, Portions);
        KcalText = Loc.Whole(macros.Kcal);
        ProteinText = Loc.Grams(macros.Protein);
        CarbsText = Loc.Grams(macros.Carbs);
        FatText = Loc.Grams(macros.Fat);
        Ingredients = _recipe.Ingredients
            .Select(i => (Food: FoodCatalog.All.FirstOrDefault(f => f.Code == i.Code), i.Grams))
            .Where(i => i.Food is not null)
            .Select(i => new IngredientLineItem(Loc.CatalogFoodName(i.Food!), Loc.Grams(i.Grams * factor)))
            .ToList();
    }

    [RelayCommand]
    private void More() => Portions = Math.Min(4, Portions + 0.25);

    [RelayCommand]
    private void Fewer() => Portions = Math.Max(0.25, Portions - 0.25);

    [RelayCommand]
    private async Task AddAsync()
    {
        if (_recipe is null || _isSaving)
            return;
        _isSaving = true;
        try
        {
            var (entry, dish) = await store.LogCatalogRecipeAsync(_recipe, Name, Portions, _date, _meal, DateTime.Now);
            await health.SaveFoodAsync(entry, Loc.FoodName(dish));
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

public sealed record IngredientLineItem(string Name, string Quantity);

public sealed record StepItem(int Number, string Text);
