using Fonte.ViewModels;

namespace Fonte.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Cheap, and keeps "Started at 18:02 · 3 exercises" and this week's days up to date.
        _viewModel.RequestReload();
    }
}
