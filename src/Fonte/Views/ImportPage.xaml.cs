using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class ImportPage : ContentPage
{
    public ImportPage(ImportViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
