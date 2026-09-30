using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class ExercisePickerPage : ContentPage
{
    public ExercisePickerPage(ExercisePickerViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = viewModel;
    }
}
