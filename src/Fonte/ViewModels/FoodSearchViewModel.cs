using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Nutrition;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>Which list the food search shows while nothing is typed.</summary>
public enum FoodShelf
{
    Recent,
    Favorites,
    Own,
    Templates,
}

/// <summary>
/// Sheet to find a food: the user's recent, favourite and own foods, the CIQUAL table (offline), a barcode scan and,
/// on demand, a search on Open Food Facts. A food opens the quantity sheet, which logs it to the meal, or adds it to
/// the dish being composed (<c>mode=ingredient</c>).
/// </summary>
public sealed partial class FoodSearchViewModel(
    FonteStore store, ProductLookup lookup, BarcodeScanner scanner, IDialogService dialogs, HealthService health)
    : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private DateTime _date = DateTime.Today;
    private Meal _meal = FoodEntryViewModel.MealAt(DateTime.Now);
    private bool _isIngredient;
    private CancellationTokenSource? _typing;
    private bool _isBusy;

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public bool CanScan { get; } = BarcodeScanner.IsSupported;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShelfVisible))]
    private bool _isSearching;

    public bool IsShelfVisible => !IsSearching;

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _shelves = [];

    [ObservableProperty]
    private IReadOnlyList<FoodItemViewModel> _foods = [];

    [ObservableProperty]
    private bool _hasNoFood;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    [ObservableProperty]
    private string _onlineText = string.Empty;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private bool _canCreateDish = true;

    private FoodShelf _shelf = FoodShelf.Recent;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("date", out var date)
            && DateTime.TryParseExact(date?.ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            _date = day;
        if (query.TryGetValue("meal", out var meal) && Enum.TryParse<Meal>(meal?.ToString(), out var m))
            _meal = m;
        _isIngredient = query.TryGetValue("mode", out var mode) && mode?.ToString() == "ingredient";
        CanCreateDish = !_isIngredient;
        Title = _isIngredient ? Loc.Get("Recipe_AddIngredient") : Loc.Meal(_meal);
        // A dish is made of foods, not of whole meals.
        Shelves = Enum.GetValues<FoodShelf>()
            .Where(s => !_isIngredient || s != FoodShelf.Templates)
            .Select((s, i) => new SelectableOption(s.ToString(), i, 3, o => ShowShelf(Enum.Parse<FoodShelf>(o.Value)))
            {
                Label = Loc.Get($"FoodShelf_{s}"),
            })
            .ToList();
        ShowShelf(_shelf);
    }

    partial void OnSearchChanged(string value)
    {
        IsOnline = false;
        _typing?.Cancel();
        _typing = new CancellationTokenSource();
        _ = SearchSoonAsync(value, _typing.Token);
    }

    private async Task SearchSoonAsync(string query, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(180, cancellation);
            await RefreshAsync(query, cancellation);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke took over.
        }
    }

    private async Task RefreshAsync(string query, CancellationToken cancellation = default)
    {
        IsSearching = !string.IsNullOrWhiteSpace(query);
        if (!IsSearching && _shelf == FoodShelf.Templates)
        {
            var templates = await store.GetMealTemplatesAsync(_meal);
            cancellation.ThrowIfCancellationRequested();
            Foods = templates.Select(t => new FoodItemViewModel(t, AddTemplateAsync, DeleteTemplateAsync)).ToList();
            HasNoFood = Foods.Count == 0;
            EmptyText = Loc.Get("FoodShelf_TemplatesEmpty");
            return;
        }
        IReadOnlyList<Food> foods = IsSearching
            ? await store.SearchFoodsAsync(query)
            : _shelf switch
            {
                FoodShelf.Favorites => await store.GetFavoriteFoodsAsync(),
                FoodShelf.Own => await store.GetOwnFoodsAsync(),
                _ => await store.GetRecentFoodsAsync(),
            };
        cancellation.ThrowIfCancellationRequested();
        Foods = foods.Select(f => new FoodItemViewModel(f, OpenFoodAsync)).ToList();
        HasNoFood = Foods.Count == 0;
        EmptyText = IsSearching
            ? Loc.Get("FoodSearch_NoResult")
            : Loc.Get($"FoodShelf_{_shelf}Empty");
        OnlineText = Loc.Format("FoodSearch_Online", query.Trim());
    }

    private async void ShowShelf(FoodShelf shelf)
    {
        _shelf = shelf;
        foreach (var option in Shelves)
            option.IsSelected = option.Value == shelf.ToString();
        try
        {
            await RefreshAsync(Search);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Food shelf failed: {ex}");
        }
    }

    private Task OpenFoodAsync(Food food) => OpenAsync(food.Id);

    /// <summary>One tap logs the whole meal template and closes the search.</summary>
    private async Task AddTemplateAsync(MealTemplateView template)
    {
        if (_isBusy)
            return;
        _isBusy = true;
        try
        {
            var entries = await store.AddMealTemplateAsync(template.Template.Id, _date, _meal, DateTime.Now);
            Palette.Haptic();
            foreach (var entry in entries)
            {
                if (template.Items.FirstOrDefault(i => i.Food.Id == entry.FoodId)?.Food is { } food)
                    await health.SaveFoodAsync(entry, Loc.FoodName(food));
            }
            SuccessToast.Show(Loc.Format("FoodEntry_Added", Loc.Meal(_meal)));
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task DeleteTemplateAsync(MealTemplateView template)
    {
        var delete = Loc.Get("MealTemplate_Delete");
        if (await dialogs.ChooseAsync(template.Template.Name, delete) != delete)
            return;
        await store.DeleteMealTemplateAsync(template.Template.Id);
        Reload();
    }

    private Task OpenAsync(int foodId) => Shell.Current.GoToAsync($"{Routes.FoodEntry}?food={foodId}&{Destination}");

    [RelayCommand]
    private async Task SearchOnlineAsync()
    {
        var query = Search.Trim();
        if (query.Length < 2 || _isBusy)
            return;
        _isBusy = IsWorking = true;
        try
        {
            var (status, products) = await lookup.SearchAsync(query);
            if (status == LookupStatus.Offline)
            {
                await dialogs.AlertAsync(Loc.Get("FoodSearch_OfflineTitle"), Loc.Get("FoodSearch_OfflineText"));
                return;
            }
            Foods = products.Select(p => new FoodItemViewModel(p, SaveAndOpenAsync)).ToList();
            HasNoFood = Foods.Count == 0;
            EmptyText = Loc.Get("FoodSearch_NoResult");
            IsOnline = true;
        }
        finally
        {
            _isBusy = IsWorking = false;
        }
    }

    private async Task SaveAndOpenAsync(Product product)
    {
        var food = await store.SaveProductAsync(product);
        await OpenAsync(food.Id);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (_isBusy)
            return;
        var barcode = await scanner.ScanAsync();
        if (string.IsNullOrEmpty(barcode))
            return;
        Palette.Haptic();

        if (await store.FindProductAsync(barcode) is { } known)
        {
            await OpenAsync(known.Id);
            return;
        }

        _isBusy = IsWorking = true;
        try
        {
            var (status, product) = await lookup.FindAsync(barcode);
            if (status == LookupStatus.Found && product is not null)
            {
                await SaveAndOpenAsync(product);
                return;
            }
            var message = status == LookupStatus.Offline ? Loc.Get("FoodSearch_ScanOffline") : Loc.Get("FoodSearch_ScanUnknown");
            if (await dialogs.ConfirmAsync(Loc.Get("FoodSearch_ScanTitle"), message, Loc.Get("FoodSearch_CreateFood")))
                await Shell.Current.GoToAsync($"{Routes.CustomFood}?{Destination}&barcode={Uri.EscapeDataString(barcode)}");
        }
        finally
        {
            _isBusy = IsWorking = false;
        }
    }

    [RelayCommand]
    private Task CreateFoodAsync() => Shell.Current.GoToAsync(
        $"{Routes.CustomFood}?{Destination}&name={Uri.EscapeDataString(Search.Trim())}");

    [RelayCommand]
    private Task CreateDishAsync() => Shell.Current.GoToAsync($"{Routes.Recipe}?{Destination}");

    /// <summary>Where a food created from here goes next: the quantity sheet, for this meal or this dish.</summary>
    private string Destination => _isIngredient ? "mode=ingredient" : $"date={NutritionViewModel.Iso(_date)}&meal={_meal}";

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    /// <summary>Lists again after a food was created, edited or starred elsewhere.</summary>
    public void Reload() => ShowShelf(_shelf);
}

/// <summary>A food of a list: the user's, CIQUAL's or an Open Food Facts product not saved yet.</summary>
public sealed class FoodItemViewModel
{
    public FoodItemViewModel(Food food, Func<Food, Task> open)
    {
        Name = Loc.FoodName(food);
        Caption = Describe(food.Brand, food.Kcal, food.ServingGrams, food.ServingName, food.Source);
        IsFavorite = food.IsFavorite;
        OpenCommand = new AsyncRelayCommand(() => open(food));
    }

    public FoodItemViewModel(MealTemplateView template, Func<MealTemplateView, Task> add, Func<MealTemplateView, Task> delete)
    {
        Name = template.Template.Name;
        Caption = $"{string.Join(", ", template.Items.Take(3).Select(i => Loc.FoodName(i.Food)))}{(template.Items.Count > 3 ? "…" : string.Empty)} · {Loc.Kcal(template.Totals.Kcal)}";
        IsTemplate = true;
        OpenCommand = new AsyncRelayCommand(() => add(template));
        DeleteCommand = new AsyncRelayCommand(() => delete(template));
    }

    public FoodItemViewModel(Product product, Func<Product, Task> open)
    {
        Name = product.Name;
        Caption = Describe(product.Brand, product.Kcal, product.ServingGrams, product.ServingName, FoodSource.Product);
        OpenCommand = new AsyncRelayCommand(() => open(product));
    }

    public string Name { get; }

    public string Caption { get; }

    public bool IsFavorite { get; }

    /// <summary>A meal template: a tap logs it, a button deletes it.</summary>
    public bool IsTemplate { get; }

    public IAsyncRelayCommand OpenCommand { get; }

    public IAsyncRelayCommand? DeleteCommand { get; }

    private static string Describe(string? brand, double kcal, double? servingGrams, string? servingName, FoodSource source)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(brand))
            parts.Add(brand);
        else if (source == FoodSource.Recipe)
            parts.Add(Loc.Get("FoodSearch_Dish"));
        parts.Add(Loc.Format("FoodSearch_Per100", Loc.Kcal(kcal)));
        if (servingGrams is { } grams && source != FoodSource.Recipe)
            parts.Add($"{servingName ?? Loc.Get("FoodEntry_Serving")} {Loc.Grams(grams)}");
        else if (servingGrams is { } dish)
            parts.Add(Loc.Format("FoodSearch_Portion", Loc.Grams(dish)));
        return string.Join(" · ", parts);
    }
}
