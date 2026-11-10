using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class MealPlannerPage : ContentPage
{
    public MealPlannerPage(MealPlannerViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
