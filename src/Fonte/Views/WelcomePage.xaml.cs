using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class WelcomePage : ContentPage
{
    public WelcomePage(WelcomeViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }

    // The welcome screens are gone through with the buttons; going back would leave the app unset.
    protected override bool OnBackButtonPressed() => true;
}
