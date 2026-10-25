using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class FoodEntryPage : ContentPage
{
    private readonly FoodEntryViewModel _viewModel;
    private bool _appeared;

    public FoodEntryPage(FoodEntryViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Back from editing the food or the dish.
        if (_appeared)
            await _viewModel.RefreshFoodAsync();
        _appeared = true;
    }
}
