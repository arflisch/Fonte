using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class WodPage : ContentPage
{
    public WodPage(WodViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
