using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class CustomFoodPage : ContentPage
{
    public CustomFoodPage(CustomFoodViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
