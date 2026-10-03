using Fonte.ViewModels;

namespace Fonte.Views;

public partial class ProgramsPage : ContentPage
{
    private readonly ProgramsViewModel _viewModel;

    public ProgramsPage(ProgramsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        // The ready-made programs go two by two.
        CatalogGrid.ChildAdded += (_, e) =>
        {
            if (e.Element is View view)
            {
                var index = CatalogGrid.Children.IndexOf(view);
                Grid.SetRow(view, index / 2);
                Grid.SetColumn(view, index % 2);
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // "Done Monday", the week of the program: dates move on.
        _viewModel.RequestReload();
    }
}
