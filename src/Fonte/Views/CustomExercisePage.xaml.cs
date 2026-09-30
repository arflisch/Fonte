using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class CustomExercisePage : ContentPage
{
    public CustomExercisePage(CustomExerciseViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
