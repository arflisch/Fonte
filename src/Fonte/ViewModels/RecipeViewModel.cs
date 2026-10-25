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

/// <summary>
/// Sheet to compose a dish from ingredients, with its weight once cooked and its number of portions; the dish is
/// then a food like any other, logged by the portion or by the gram.
/// </summary>
public sealed partial class RecipeViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private readonly FonteStore _store;
    private readonly IDialogService _dialogs;
    private int? _recipeId;
    private string? _destination;
    private bool _isSaving;
    private bool _isApplied;

    public RecipeViewModel(FonteStore store, IDialogService dialogs)
    {
        _store = store;
        _dialogs = dialogs;
        WeakReferenceMessenger.Default.Register<RecipeViewModel, IngredientPickedMessage>(
            this, static (recipe, message) => recipe.OnIngredientPicked(message));
    }

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public ObservableCollection<IngredientItem> Ingredients { get; } = [];

    [ObservableProperty]
    private string _title = Loc.Get("Recipe_New");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private int _servings = 2;

    [ObservableProperty]
    private string _cookedWeight = string.Empty;

    [ObservableProperty]
    private string _rawWeightText = string.Empty;

    [ObservableProperty]
    private bool _hasIngredients;

    [ObservableProperty]
    private string _totalText = string.Empty;

    [ObservableProperty]
    private string _portionText = string.Empty;

    [ObservableProperty]
    private string _portionMacrosText = string.Empty;

    [ObservableProperty]
    private bool _canRemove;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public string ServingsText => Loc.Format("Recipe_Servings", Servings);

    partial void OnServingsChanged(int value)
    {
        OnPropertyChanged(nameof(ServingsText));
        Refresh();
    }

    partial void OnCookedWeightChanged(string value) => Refresh();

    partial void OnNameChanged(string value) => HasError = false;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (_isApplied)
            return;
        _isApplied = true;
        if (query.TryGetValue("date", out var date) && query.TryGetValue("meal", out var meal))
            _destination = $"date={date}&meal={meal}";
        if (!query.TryGetValue("id", out var id) || !int.TryParse(id?.ToString(), out var recipeId))
            return;
        if (await _store.GetFoodAsync(recipeId) is not { Source: FoodSource.Recipe } recipe)
            return;

        _recipeId = recipe.Id;
        Title = Loc.Get("Recipe_Edit");
        Name = recipe.Name;
        CanRemove = true;
        foreach (var part in await _store.GetRecipeAsync(recipe.Id))
            Ingredients.Add(new IngredientItem(part.Food, part.Grams, EditIngredientAsync));
        var raw = Ingredients.Sum(i => i.Grams);
        if (recipe.TotalGrams is { } total && Math.Abs(total - raw) > 0.5)
            CookedWeight = Loc.Number(total);
        if (recipe.TotalGrams is { } all && recipe.ServingGrams is > 0)
            Servings = Math.Max(1, (int)Math.Round(all / recipe.ServingGrams.Value));
        Refresh();
    }

    private void OnIngredientPicked(IngredientPickedMessage message) => MainThread.BeginInvokeOnMainThread(async () =>
    {
        if (await _store.GetFoodAsync(message.FoodId) is not { } food)
            return;
        Ingredients.Add(new IngredientItem(food, message.Grams, EditIngredientAsync));
        HasError = false;
        Refresh();
    });

    private async Task EditIngredientAsync(IngredientItem item)
    {
        var change = Loc.Get("Recipe_ChangeQuantity");
        var choice = await _dialogs.ChooseAsync(item.Name, Loc.Get("Recipe_RemoveIngredient"), change);
        if (choice == change)
        {
            if (await _dialogs.PromptNumberAsync(item.Name, Loc.Get("Recipe_QuantityPrompt"), item.Grams) is { } grams and <= 5000)
            {
                Ingredients[Ingredients.IndexOf(item)] = new IngredientItem(item.Food, grams, EditIngredientAsync);
                Refresh();
            }
        }
        else if (choice is not null)
        {
            Ingredients.Remove(item);
            Refresh();
        }
    }

    private double? Cooked =>
        double.TryParse(CookedWeight.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var grams)
        && double.IsFinite(grams) && grams > 0
            ? grams
            : null;

    private void Refresh()
    {
        HasIngredients = Ingredients.Count > 0;
        var raw = Ingredients.Sum(i => i.Grams);
        RawWeightText = Loc.Format("Recipe_RawWeight", Loc.Grams(raw));
        var kcal = Ingredients.Sum(i => Energy.Scale(i.Food.Kcal, i.Grams));
        var protein = Ingredients.Sum(i => Energy.Scale(i.Food.Protein, i.Grams));
        var carbs = Ingredients.Sum(i => Energy.Scale(i.Food.Carbs, i.Grams));
        var fat = Ingredients.Sum(i => Energy.Scale(i.Food.Fat, i.Grams));
        var total = Cooked ?? raw;
        TotalText = Loc.Format("Recipe_Total", Loc.Kcal(kcal), Loc.Grams(total));
        var n = Math.Max(1, Servings);
        PortionText = Loc.Format("Recipe_Portion", Loc.Grams(total / n), Loc.Kcal(kcal / n));
        PortionMacrosText = Loc.Format("Nutrition_GoalMacros",
            Loc.Whole(protein / n), Loc.Whole(carbs / n), Loc.Whole(fat / n));
    }

    [RelayCommand]
    private Task AddIngredientAsync() => Shell.Current.GoToAsync($"{Routes.FoodSearch}?mode=ingredient");

    [RelayCommand]
    private void MoreServings()
    {
        if (Servings < 30)
            Servings++;
    }

    [RelayCommand]
    private void FewerServings()
    {
        if (Servings > 1)
            Servings--;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_isSaving)
            return;
        _isSaving = true;
        try
        {
            var recipe = await _store.SaveRecipeAsync(_recipeId, Name, Ingredients.Select(i => (i.Food.Id, i.Grams)).ToList(),
                Cooked, Servings, DateTime.Now);
            Palette.Haptic();
            SuccessToast.Show(Loc.Get("Recipe_Saved"));
            // Created from the search: go straight on to how much of it was eaten.
            await Shell.Current.GoToAsync(_recipeId is null && _destination is not null
                ? $"../{Routes.FoodEntry}?food={recipe.Id}&{_destination}"
                : "..");
        }
        catch (FonteException ex)
        {
            ErrorMessage = Loc.Error(ex);
            HasError = true;
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (_recipeId is not { } id)
            return;
        if (!await _dialogs.ConfirmAsync(Name, Loc.Get("CustomFood_RemoveConfirm"), Loc.Get("CustomFood_Remove")))
            return;
        await _store.RemoveFoodAsync(id);
        Palette.Haptic();
        await Shell.Current.GoToAsync("../..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");
}

/// <summary>An ingredient of a dish and how much of it goes in.</summary>
public sealed class IngredientItem
{
    public IngredientItem(Food food, double grams, Func<IngredientItem, Task> edit)
    {
        Food = food;
        Grams = grams;
        Name = Loc.FoodName(food);
        Detail = Loc.Grams(grams);
        KcalText = Loc.Kcal(Energy.Scale(food.Kcal, grams));
        EditCommand = new AsyncRelayCommand(() => edit(this));
    }

    public Food Food { get; }

    public double Grams { get; }

    public string Name { get; }

    public string Detail { get; }

    public string KcalText { get; }

    public IAsyncRelayCommand EditCommand { get; }
}
