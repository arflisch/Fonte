using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class NutritionGoalPage : ContentPage
{
    public NutritionGoalPage(NutritionGoalViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
