using Fonte.Controls;
using Fonte.ViewModels;

namespace Fonte.Views;

public partial class BackupPasswordPage : ContentPage
{
    private readonly BackupPasswordViewModel _viewModel;

    public BackupPasswordPage(BackupPasswordViewModel viewModel)
    {
        InitializeComponent();
        Sheet.Adapt(this);
        BindingContext = _viewModel = viewModel;
        // When choosing a password, Return moves on to the confirmation, which the keyboard would otherwise hide.
        PasswordEntry.ReturnType = viewModel.IsCreate ? ReturnType.Next : ReturnType.Done;
        PasswordEntry.Focused += (_, _) => KeepInView(viewModel.IsCreate ? ConfirmationField : PasswordField);
        ConfirmationEntry.Focused += (_, _) => KeepInView(ConfirmationField);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Let the sheet finish sliding in before bringing up the keyboard.
        await Task.Delay(400);
        PasswordEntry.Focus();
    }

    /// <summary>
    /// The keyboard shrinks the sheet to the part above it, where the fields would be scrolled out of sight: bring
    /// them back once the keyboard is up. Showing the confirmation shows the password above it too.
    /// </summary>
    private async void KeepInView(VisualElement field)
    {
        await Task.Delay(350);
        // Vertically only: scrolling to the element itself also shifts the content sideways on iOS.
        var bottom = field.Y + field.Height + 12;
        await Scroller.ScrollToAsync(0, Math.Max(0, bottom - Scroller.Height), animated: true);
    }

    private void OnPasswordCompleted(object? sender, EventArgs e)
    {
        if (_viewModel.IsCreate)
            ConfirmationEntry.Focus();
        else
            _viewModel.ConfirmCommand.Execute(null);
    }
}
