using Fonte.ViewModels;

namespace Fonte.Views;

public partial class ExercisesPage : ContentPage
{
    private readonly ExercisesViewModel _viewModel;

    public ExercisesPage(ExercisesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.IsLoaded)
            _viewModel.RequestReload();
    }
}
