using Fonte.ViewModels;

namespace Fonte.Views;

public partial class WorkoutPage : ContentPage
{
    private readonly WorkoutViewModel _viewModel;

    public WorkoutPage(WorkoutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        // Exercises just picked go at the end: bring them into view, above the "Add an exercise" button.
        viewModel.ExercisesAdded += async (_, _) =>
        {
            await Task.Delay(250);
            var target = Math.Min(
                AddExerciseButton.Y + AddExerciseButton.Height + 24 - Scroll.Height,
                Scroll.ContentSize.Height - Scroll.Height);
            if (target > Scroll.ScrollY)
                await Scroll.ScrollToAsync(0, target, true);
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Activate();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Deactivate();
    }
}
