using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class SummaryPage : ContentPage
{
    public SummaryPage(SummaryViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
