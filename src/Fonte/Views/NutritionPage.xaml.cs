using Fonte.ViewModels;

namespace Fonte.Views;

public partial class NutritionPage : ContentPage
{
    private readonly NutritionViewModel _viewModel;

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
    }
}
