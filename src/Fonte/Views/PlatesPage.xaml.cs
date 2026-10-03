using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class PlatesPage : ContentPage
{
    public PlatesPage(PlatesViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
