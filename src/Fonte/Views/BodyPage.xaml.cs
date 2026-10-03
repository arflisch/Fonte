using Fonte.ViewModels;

namespace Fonte.Views;

public partial class BodyPage : ContentPage
{
    private readonly BodyViewModel _viewModel;

    public BodyPage(BodyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        // Measurements go two by two, photos three by three.
        Arrange(MeasurementsGrid, 2);
        Arrange(PhotosGrid, 3);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RequestReload();
    }

    private static void Arrange(Grid grid, int columns)
    {
        grid.ChildAdded += (_, _) => Place(grid, columns);
        grid.ChildRemoved += (_, _) => Place(grid, columns);
    }

    private static void Place(Grid grid, int columns)
    {
        var rows = (grid.Children.Count + columns - 1) / columns;
        while (grid.RowDefinitions.Count < rows)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < grid.Children.Count; i++)
        {
            if (grid.Children[i] is View view)
            {
                Grid.SetRow(view, i / columns);
                Grid.SetColumn(view, i % columns);
            }
        }
    }
}
