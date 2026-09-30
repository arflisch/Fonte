using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fonte.ViewModels;

/// <summary>An item of a picker grid (emoji, colour, mode…) laid out at a fixed row and column.</summary>
public sealed partial class SelectableOption(string value, int index, int columns, Action<SelectableOption> onSelect)
    : ObservableObject
{
    public string Value { get; } = value;

    public string Label { get; init; } = value;

    public Color Color { get; } = value.StartsWith('#') ? Color.FromArgb(value) : Colors.Transparent;

    public int Row { get; } = index / columns;

    public int Column { get; } = index % columns;

    public double LabelOpacity => IsSelected ? 1 : 0.55;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelOpacity))]
    private bool _isSelected;

    /// <summary>Highlight colour of the selection (the pocket colour being edited).</summary>
    [ObservableProperty]
    private Color _accent = Colors.Transparent;

    [ObservableProperty]
    private Color _accentSoft = Colors.Transparent;

    [RelayCommand]
    private void Select() => onSelect(this);

    public static IReadOnlyList<SelectableOption> Grid(IEnumerable<string> values, int columns, Action<SelectableOption> onSelect) =>
        values.Select((v, i) => new SelectableOption(v, i, columns, onSelect)).ToList();
}
