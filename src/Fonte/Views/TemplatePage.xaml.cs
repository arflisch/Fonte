using Fonte.ViewModels;

namespace Fonte.Views;

public partial class TemplatePage : ContentPage
{
    public TemplatePage(TemplateViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
