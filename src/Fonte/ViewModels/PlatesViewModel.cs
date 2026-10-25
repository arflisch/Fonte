using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>Which plates to put on each side of the bar for a load, and the warm-up sets that lead to it.</summary>
public sealed partial class PlatesViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private readonly AppSettings _settings;
    private double _bar;

    public PlatesViewModel(AppSettings settings)
    {
        _settings = settings;
        _bar = settings.BarWeight;
        var bars = Loc.Pounds ? Plates.BarsPounds : Plates.Bars;
        Bars = bars
            .Select((b, i) => new SelectableOption(b.ToString(CultureInfo.InvariantCulture), i, bars.Count, SelectBar)
            {
                Label = Amount(b),
                IsSelected = Math.Abs(b - _bar) < 0.01,
            })
            .ToList();
        _targetText = Loc.Number(Math.Max(_bar, Loc.Pounds ? 135 : 60));
        Update();
    }

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    public IReadOnlyList<SelectableOption> Bars { get; }

    /// <summary>The calculator works in the unit shown: a gym in pounds has pound plates.</summary>
    private static double Step => Loc.Pounds ? 5 : 2.5;

    public string Unit { get; } = Loc.WeightUnit;

    public string RemoveText { get; } = $"−{Loc.Number(Step)}";

    public string AddText { get; } = $"+{Loc.Number(Step)}";

    [ObservableProperty]
    private string _targetText;

    [ObservableProperty]
    private IReadOnlyList<PlateItem> _perSide = [];

    [ObservableProperty]
    private IReadOnlyList<PlateRow> _rows = [];

    [ObservableProperty]
    private bool _hasPlates;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _warmUpText = string.Empty;

    [ObservableProperty]
    private bool _hasWarmUp;

    [ObservableProperty]
    private string _platesText = string.Empty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("weight", out var raw)
            && double.TryParse(raw?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
            && weight > 0)
            TargetText = Loc.WeightNumber(weight);
    }

    partial void OnTargetTextChanged(string value) => Update();

    private double Target =>
        double.TryParse(TargetText?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value) ? Math.Clamp(value, 0, 1000) : 0;

    private void SelectBar(SelectableOption option)
    {
        foreach (var bar in Bars)
            bar.IsSelected = bar == option;
        _bar = double.Parse(option.Value, CultureInfo.InvariantCulture);
        _settings.BarWeight = _bar;
        Palette.Haptic();
        Update();
    }

    private void Update()
    {
        var plates = _settings.Plates;
        var target = Target;
        var load = Plates.Load(target, _bar, plates);
        PerSide = load.PerSide.Select(p => new PlateItem(Kilograms(p))).ToList();
        Rows = load.PerSide.GroupBy(p => p).Select(g => new PlateRow($"{Amount(g.Key)} × {g.Count()}", PlateItem.ColorOf(Kilograms(g.Key)))).ToList();
        HasPlates = PerSide.Count > 0;
        ResultText = target <= _bar
            ? Loc.Get("Plates_BarOnly")
            : load.Missing(target) > 0
                ? Loc.Format("Plates_Closest", Amount(load.Achieved))
                : Loc.Format("Plates_PerSide", Amount(load.PerSide.Sum()));
        var warmUp = Plates.WarmUp(load.Achieved, _bar, plates);
        HasWarmUp = warmUp.Count > 0;
        WarmUpText = string.Join("  ·  ", warmUp.Select((s, i) =>
            $"{(i == 0 ? Loc.Get("Plates_Bar") : Loc.Number(s.Weight))} × {s.Reps}"));
        PlatesText = Loc.Format("Plates_Available", string.Join(" · ", plates.Select(Loc.Number)), Loc.WeightUnit);
    }

    /// <summary>A weight already in the unit shown: "20 kg", "45 lb".</summary>
    private static string Amount(double value) => $"{Loc.Number(value)} {Loc.WeightUnit}";

    /// <summary>A plate's weight in kilograms, which sets its size and colour (a 45 lb plate looks like a 20 kg one).</summary>
    private static double Kilograms(double value) => Loc.TypedWeight(value);

    [RelayCommand]
    private void Add() => Change(Step);

    [RelayCommand]
    private void Remove() => Change(-Step);

    private void Change(double delta)
    {
        TargetText = Loc.Number(Math.Max(0, Math.Round((Target + delta) / Step) * Step));
        Palette.Haptic();
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}

/// <summary>A plate drawn on the bar: taller and wider when heavier, in the usual competition colours.</summary>
public sealed class PlateItem(double weight)
{
    public double Height { get; } = weight switch
    {
        >= 15 => 130,
        >= 10 => 110,
        >= 5 => 80,
        >= 2.5 => 64,
        _ => 52,
    };

    public double Width { get; } = weight >= 20 ? 20 : weight >= 10 ? 16 : 12;

    public Color Color { get; } = ColorOf(weight);

    public static Color ColorOf(double weight) => weight switch
    {
        >= 25 => Color.FromArgb("#E24B4A"),
        >= 20 => Color.FromArgb("#378ADD"),
        >= 15 => Color.FromArgb("#E8B22C"),
        >= 10 => Color.FromArgb("#1D9E75"),
        >= 5 => Color.FromArgb("#CBD5E1"),
        >= 2.5 => Color.FromArgb("#334155"),
        _ => Color.FromArgb("#94A3B8"),
    };
}

public sealed record PlateRow(string Text, Color Color);
