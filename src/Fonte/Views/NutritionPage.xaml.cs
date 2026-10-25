using Fonte.ViewModels;

namespace Fonte.Views;

public partial class NutritionPage : ContentPage
{
    private readonly NutritionViewModel _viewModel;
    private IDispatcherTimer? _clock;

    public NutritionPage(NutritionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        // Protein, carbohydrates and fat side by side.
        MacrosGrid.ChildAdded += (_, _) =>
        {
            for (var i = 0; i < MacrosGrid.Children.Count; i++)
                Grid.SetColumn((View)MacrosGrid.Children[i], i);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RequestReload();
        // The fasting card counts the hours as they go.
        _clock ??= Dispatcher.CreateTimer();
        _clock.Interval = TimeSpan.FromMinutes(1);
        _clock.Tick += OnTick;
        _clock.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_clock is not null)
        {
            _clock.Stop();
            _clock.Tick -= OnTick;
        }
    }

    private void OnTick(object? sender, EventArgs e) => _viewModel.UpdateFasting();
}
