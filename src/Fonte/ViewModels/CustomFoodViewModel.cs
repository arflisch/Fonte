using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>
/// Sheet to create one of the user's foods from its label (per 100 g), or to edit it. Created from the search, the
/// food then opens in the quantity sheet.
/// </summary>
public sealed partial class CustomFoodViewModel(FonteStore store, IDialogService dialogs)
    : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private Food _food = new();
    private string? _destination;
    private bool _isSaving;
    private bool _isApplied;

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    [ObservableProperty]
    private string _title = Loc.Get("CustomFood_New");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _brand = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KcalPlaceholder))]
    private string _protein = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KcalPlaceholder))]
    private string _carbs = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KcalPlaceholder))]
    private string _fat = string.Empty;

    [ObservableProperty]
    private string _kcal = string.Empty;

    [ObservableProperty]
    private string _sugars = string.Empty;

    [ObservableProperty]
    private string _fiber = string.Empty;

    [ObservableProperty]
    private string _salt = string.Empty;

    [ObservableProperty]
    private string _servingName = string.Empty;

    [ObservableProperty]
    private string _servingGrams = string.Empty;

    [ObservableProperty]
    private string _barcodeText = string.Empty;

    [ObservableProperty]
    private bool _hasBarcode;

    [ObservableProperty]
    private bool _canRemove;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    /// <summary>Left empty, the calories are worked out from the macronutrients (4, 4 and 9 kcal per gram).</summary>
    public string KcalPlaceholder => Computed is { } kcal ? Loc.Number(Math.Round(kcal)) : "0";

    private double? Computed =>
        Number(Protein) is null && Number(Carbs) is null && Number(Fat) is null
            ? null
            : (Number(Protein) ?? 0) * 4 + (Number(Carbs) ?? 0) * 4 + (Number(Fat) ?? 0) * 9;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (_isApplied)
            return;
        _isApplied = true;
        if (query.TryGetValue("mode", out var mode) && mode?.ToString() == "ingredient")
            _destination = "mode=ingredient";
        else if (query.TryGetValue("date", out var date) && query.TryGetValue("meal", out var meal))
            _destination = $"date={date}&meal={meal}";
        if (query.TryGetValue("name", out var name))
            Name = Uri.UnescapeDataString(name?.ToString() ?? string.Empty);
        if (query.TryGetValue("barcode", out var barcode) && barcode?.ToString() is { Length: > 0 } code)
            _food = new Food { Source = FoodSource.Product, Code = Uri.UnescapeDataString(code) };

        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var foodId)
            && await store.GetFoodAsync(foodId) is { Source: FoodSource.Custom or FoodSource.Product } food)
        {
            _food = food;
            Title = Loc.Get("CustomFood_Edit");
            Name = food.Name;
            Brand = food.Brand ?? string.Empty;
            Kcal = Loc.Number(food.Kcal);
            Protein = Loc.Number(food.Protein);
            Carbs = Loc.Number(food.Carbs);
            Fat = Loc.Number(food.Fat);
            Sugars = Show(food.Sugars);
            Fiber = Show(food.Fiber);
            Salt = Show(food.Salt);
            ServingName = food.ServingName ?? string.Empty;
            ServingGrams = Show(food.ServingGrams);
            CanRemove = true;
        }
        HasBarcode = _food.Code is not null;
        BarcodeText = Loc.Format("CustomFood_Barcode", _food.Code ?? string.Empty);
    }

    partial void OnNameChanged(string value) => HasError = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_isSaving)
            return;
        var kcal = Number(Kcal) ?? Computed;
        if (kcal is null)
        {
            ErrorMessage = Loc.Get("CustomFood_NeedsEnergy");
            HasError = true;
            return;
        }
        _isSaving = true;
        try
        {
            var isNew = _food.Id == 0;
            _food.Name = Name;
            _food.Brand = Brand;
            _food.Kcal = Math.Round(kcal.Value, 1);
            _food.Protein = Number(Protein) ?? 0;
            _food.Carbs = Number(Carbs) ?? 0;
            _food.Fat = Number(Fat) ?? 0;
            _food.Sugars = Number(Sugars);
            _food.Fiber = Number(Fiber);
            _food.Salt = Number(Salt);
            _food.ServingGrams = Number(ServingGrams);
            _food.ServingName = _food.ServingGrams is null || string.IsNullOrWhiteSpace(ServingName) ? null : ServingName.Trim();
            var saved = await store.SaveCustomFoodAsync(_food);
            Palette.Haptic();
            SuccessToast.Show(Loc.Get("CustomFood_Saved"));
            // Created from the search: go straight on to how much of it was eaten.
            await Shell.Current.GoToAsync(isNew && _destination is not null
                ? $"../{Routes.FoodEntry}?food={saved.Id}&{_destination}"
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
        if (_food.Id == 0)
            return;
        if (!await dialogs.ConfirmAsync(_food.Name, Loc.Get("CustomFood_RemoveConfirm"), Loc.Get("CustomFood_Remove")))
            return;
        await store.RemoveFoodAsync(_food.Id);
        Palette.Haptic();
        // The quantity sheet of a removed food has nothing left to show.
        await Shell.Current.GoToAsync("../..");
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private static string Show(double? value) => value is { } v ? Loc.Number(v) : string.Empty;

    private static double? Number(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) && value >= 0
            ? value
            : null;
}
