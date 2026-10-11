using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class ProPage : ContentPage
{
    public ProPage(ProViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
