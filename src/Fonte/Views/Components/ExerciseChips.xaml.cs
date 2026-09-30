using Fonte.ViewModels;

namespace Fonte.Views.Components;

public partial class ExerciseChips : VerticalStackLayout
{
    public static readonly BindableProperty SearchProperty =
        BindableProperty.Create(nameof(Search), typeof(string), typeof(ExerciseChips), string.Empty, BindingMode.TwoWay,
            propertyChanged: (b, _, n) => ((ExerciseChips)b).SearchEntry.Text = (string)n);

    public static readonly BindableProperty FilterProperty =
        BindableProperty.Create(nameof(Filter), typeof(ExerciseFilter), typeof(ExerciseChips), null,
            propertyChanged: (b, _, n) => BindableLayout.SetItemsSource(((ExerciseChips)b).ChipsLayout, ((ExerciseFilter?)n)?.Chips));

    public ExerciseChips()
    {
        InitializeComponent();
        SearchEntry.TextChanged += (_, e) => Search = e.NewTextValue ?? string.Empty;
    }

    public string Search
    {
        get => (string)GetValue(SearchProperty);
        set => SetValue(SearchProperty, value);
    }

    public ExerciseFilter? Filter
    {
        get => (ExerciseFilter?)GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }
}
