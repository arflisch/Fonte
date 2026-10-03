using Fonte.Services;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private readonly AppSettings _settings;
    private bool _welcomeShown;

    public HomePage(HomeViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _settings = settings;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Cheap, and keeps "Started at 18:02 · 3 exercises" and this week's days up to date.
        _viewModel.RequestReload();

        if (!_settings.HasOnboarded && !_welcomeShown)
        {
            _welcomeShown = true;
            // Let the first frame appear: a sheet presented during launch is dropped by iOS.
            await Task.Delay(300);
            await Shell.Current.GoToAsync(Routes.Welcome);
        }
    }
}
