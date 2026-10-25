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

/// <summary>Sent when a food is picked for the dish being composed.</summary>
public sealed record IngredientPickedMessage(int FoodId, double Grams);

/// <summary>
/// Sheet to say how much of a food was eaten and at which meal (or how much goes into a dish), with its calories
/// and macronutrients computed as the quantity changes. Also edits or deletes an entry of the journal.
/// </summary>
public sealed partial class FoodEntryViewModel(FonteStore store, HealthService health, IDialogService dialogs)
    : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private Food? _food;
    private FoodEntry? _entry;
    private DateTime _date = DateTime.Today;
    private Meal _meal = MealAt(DateTime.Now);
    private bool _isIngredient;
    private bool _isSaving;
    private bool _isApplied;

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _caption = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private string _quantity = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _portions = [];

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _meals = [];

    [ObservableProperty]
    private bool _showsMeals;

    [ObservableProperty]
    private string _kcalText = "0";

    [ObservableProperty]
    private string _proteinText = "0 g";

    [ObservableProperty]
    private string _carbsText = "0 g";

    [ObservableProperty]
    private string _fatText = "0 g";

    [ObservableProperty]
    private string _detailsText = string.Empty;

    [ObservableProperty]
    private bool _hasDetails;

    [ObservableProperty]
    private string _actionText = string.Empty;

    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private string _editFoodText = string.Empty;

    [ObservableProperty]
    private bool _canEditFood;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>The meal most likely eaten at this time of day.</summary>
    public static Meal MealAt(DateTime now) => now.TimeOfDay switch
    {
        { TotalHours: < 10.5 } => Meal.Breakfast,
        { TotalHours: < 15 } => Meal.Lunch,
        { TotalHours: < 18 } => Meal.Snack,
        _ => Meal.Dinner,
    };

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // Shell may hand the parameters over twice.
        if (_isApplied)
            return;
        _isApplied = true;
        try
        {
            if (query.TryGetValue("date", out var date)
                && DateTime.TryParseExact(date?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                _date = day;
            if (query.TryGetValue("meal", out var meal) && Enum.TryParse<Meal>(meal?.ToString(), out var m))
                _meal = m;
            _isIngredient = query.TryGetValue("mode", out var mode) && mode?.ToString() == "ingredient";

            double? grams = null;
            if (query.TryGetValue("entry", out var e) && int.TryParse(e?.ToString(), out var entryId))
            {
                _entry = await store.GetFoodEntryAsync(entryId);
                if (_entry is null)
                    return;
                _date = _entry.Date;
                _meal = _entry.Meal;
                grams = _entry.Grams;
                _food = await store.GetFoodAsync(_entry.FoodId);
            }
            else if (query.TryGetValue("food", out var f) && int.TryParse(f?.ToString(), out var foodId))
            {
                _food = await store.GetFoodAsync(foodId);
            }
            if (_food is null)
                return;

            Name = Loc.FoodName(_food);
            Caption = _food.Brand ?? (_food.Source == FoodSource.Recipe ? Loc.Get("FoodSearch_Dish") : string.Empty);
            IsFavorite = _food.IsFavorite;
            CanDelete = _entry is not null;
            ShowsMeals = !_isIngredient;
            ActionText = _isIngredient ? Loc.Get("FoodEntry_AddToDish") : _entry is null ? Loc.Get("FoodEntry_Add") : Loc.Get("Common_Save");
            CanEditFood = _food.Source is FoodSource.Custom or FoodSource.Recipe or FoodSource.Product;
            EditFoodText = _food.Source == FoodSource.Recipe ? Loc.Get("FoodEntry_EditDish") : Loc.Get("FoodEntry_EditFood");
            Meals = Enum.GetValues<Meal>()
                .Select((x, i) => new SelectableOption(x.ToString(), i, 4, o => { _meal = Enum.Parse<Meal>(o.Value); ShowMeal(); })
                {
                    Label = Loc.Meal(x),
                })
                .ToList();
            ShowMeal();
            Portions = BuildPortions(_food);
            Quantity = Loc.Number(grams ?? _food.ServingGrams ?? 100);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Food entry failed to open: {ex}");
        }
    }

    private IReadOnlyList<SelectableOption> BuildPortions(Food food)
    {
        var portions = new List<(double Grams, string Label)>();
        if (food.ServingGrams is { } serving)
        {
            var name = food.Source == FoodSource.Recipe ? Loc.Get("FoodEntry_Portion") : food.ServingName ?? Loc.Get("FoodEntry_Serving");
            portions.Add((serving, $"1 {name} · {Loc.Grams(serving)}"));
            portions.Add((serving / 2, $"½ · {Loc.Grams(serving / 2)}"));
            portions.Add((serving * 2, $"2 · {Loc.Grams(serving * 2)}"));
        }
        foreach (var grams in new double[] { 50, 100, 150, 200 })
        {
            if (portions.All(p => Math.Abs(p.Grams - grams) > 0.5))
                portions.Add((grams, Loc.Grams(grams)));
        }
        return portions
            .Select((p, i) => new SelectableOption(p.Grams.ToString(CultureInfo.InvariantCulture), i, int.MaxValue, o =>
            {
                Quantity = Loc.Number(Math.Round(double.Parse(o.Value, CultureInfo.InvariantCulture), 1));
                Palette.Haptic();
            })
            {
                Label = p.Label,
            })
            .ToList();
    }

    private void ShowMeal()
    {
        foreach (var option in Meals)
            option.IsSelected = option.Value == _meal.ToString();
    }

    private double? Grams =>
        double.TryParse(Quantity.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var grams)
        && double.IsFinite(grams) && grams > 0 && grams <= 5000
            ? grams
            : null;

    partial void OnQuantityChanged(string value)
    {
        HasError = false;
        if (_food is null)
            return;
        var grams = Grams ?? 0;
        foreach (var option in Portions)
            option.IsSelected = Math.Abs(double.Parse(option.Value, CultureInfo.InvariantCulture) - grams) < 0.05;
        KcalText = Loc.Whole(Energy.Scale(_food.Kcal, grams));
        ProteinText = Loc.Grams(Energy.Scale(_food.Protein, grams));
        CarbsText = Loc.Grams(Energy.Scale(_food.Carbs, grams));
        FatText = Loc.Grams(Energy.Scale(_food.Fat, grams));
        var details = new List<string>();
        if (_food.Sugars is { } sugars)
            details.Add(Loc.Format("FoodEntry_Sugars", Loc.Grams(Energy.Scale(sugars, grams))));
        if (_food.Fiber is { } fiber)
            details.Add(Loc.Format("FoodEntry_Fiber", Loc.Grams(Energy.Scale(fiber, grams))));
        if (_food.Salt is { } salt)
            details.Add(Loc.Format("FoodEntry_Salt", Loc.Grams(Energy.Scale(salt, grams))));
        DetailsText = string.Join(" · ", details);
        HasDetails = details.Count > 0;
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (_food is null)
            return;
        IsFavorite = !IsFavorite;
        Palette.Haptic();
        await store.SetFavoriteAsync(_food.Id, IsFavorite);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_food is null || _isSaving)
            return;
        if (Grams is not { } grams)
        {
            ErrorMessage = Loc.Get("Error_InvalidQuantity");
            HasError = true;
            return;
        }
        _isSaving = true;
        try
        {
            if (_isIngredient)
            {
                WeakReferenceMessenger.Default.Send(new IngredientPickedMessage(_food.Id, grams));
                Palette.Haptic();
                await Shell.Current.GoToAsync("../..");
                return;
            }

            var entry = _entry is null
                ? await store.AddFoodEntryAsync(_date, _meal, _food.Id, grams, DateTime.Now)
                : await store.UpdateFoodEntryAsync(_entry.Id, grams, _meal, DateTime.Now);
            Palette.Haptic();
            await health.SaveFoodAsync(entry, Loc.FoodName(_food));
            if (_entry is null)
                SuccessToast.Show(Loc.Format("FoodEntry_Added", Loc.Meal(_meal)));
            // A new entry was reached through the search: close both sheets.
            await Shell.Current.GoToAsync(_entry is null ? "../.." : "..");
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
    private async Task DeleteAsync()
    {
        if (_entry is null)
            return;
        if (!await dialogs.ConfirmAsync(Name, Loc.Get("FoodEntry_DeleteConfirm"), Loc.Get("FoodEntry_Delete")))
            return;
        if (await store.DeleteFoodEntryAsync(_entry.Id) is { } removed)
            await health.DeleteFoodsAsync([removed]);
        Palette.Haptic();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task EditFoodAsync() => _food is null
        ? Task.CompletedTask
        : Shell.Current.GoToAsync(_food.Source == FoodSource.Recipe
            ? $"{Routes.Recipe}?id={_food.Id}"
            : $"{Routes.CustomFood}?id={_food.Id}");

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    /// <summary>Shows the food again after it was edited.</summary>
    public async Task RefreshFoodAsync()
    {
        if (_food is null || await store.GetFoodAsync(_food.Id) is not { } food)
            return;
        _food = food;
        Name = Loc.FoodName(food);
        Caption = food.Brand ?? (food.Source == FoodSource.Recipe ? Loc.Get("FoodSearch_Dish") : string.Empty);
        Portions = BuildPortions(food);
        OnQuantityChanged(Quantity);
    }
}
