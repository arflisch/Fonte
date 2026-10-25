using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class RecipePage : ContentPage
{
    public RecipePage(RecipeViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
