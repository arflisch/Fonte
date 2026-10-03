using Fonte.ViewModels;

namespace Fonte.Views;

public partial class HistoryPage : ContentPage
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.RequestReload();
    }
}
