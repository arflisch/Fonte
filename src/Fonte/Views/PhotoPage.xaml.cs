using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class PhotoPage : ContentPage
{
    public PhotoPage(PhotoViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
