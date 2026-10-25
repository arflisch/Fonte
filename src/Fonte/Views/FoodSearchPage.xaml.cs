using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class FoodSearchPage : ContentPage
{
    private readonly FoodSearchViewModel _viewModel;
    private bool _appeared;

    public FoodSearchPage(FoodSearchViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Back from a food: it may have been starred, edited or eaten.
        if (_appeared)
            _viewModel.Reload();
        _appeared = true;
    }
}
