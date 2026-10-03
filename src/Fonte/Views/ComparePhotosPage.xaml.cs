using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class ComparePhotosPage : ContentPage
{
    public ComparePhotosPage(ComparePhotosViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
        _ = viewModel.LoadAsync();
    }
}
