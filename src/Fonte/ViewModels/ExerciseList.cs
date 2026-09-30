using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Controls;
using Fonte.Core.Models;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>An exercise in the library or the picker.</summary>
public sealed partial class ExerciseItemViewModel : ObservableObject
{
    public ExerciseItemViewModel(ExerciseOverview overview, Action<ExerciseItemViewModel> tapped)
    {
        var exercise = overview.Exercise;
        Id = exercise.Id;
        Muscle = exercise.Muscle;
        Name = Loc.ExerciseName(exercise);
        Icon = Icons.For(exercise.Equipment);
        Color = Palette.Muscle(exercise.Muscle);
        SoftColor = Palette.Soft(Color);
        var equipment = Loc.Equipment(exercise.Equipment);
        Caption = overview.Best is { } best ? $"{equipment} · {Loc.Format("Library_Best", Loc.Set(exercise, best))}" : equipment;
        SearchText = TextSearch.Normalize($"{Name} {Loc.Muscle(exercise.Muscle)} {equipment}");
        TapCommand = new RelayCommand(() => tapped(this));
    }

    public int Id { get; }

    public MuscleGroup Muscle { get; }

    public string Name { get; }

    public string Icon { get; }

    public Color Color { get; }

    public Color SoftColor { get; }

    public string Caption { get; }

    public string SearchText { get; }

    public IRelayCommand TapCommand { get; }

    /// <summary>Ticked in the picker.</summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Exercises of one muscle group, under a header.</summary>
public sealed class ExerciseGroup(string title, Color color, IEnumerable<ExerciseItemViewModel> items)
    : List<ExerciseItemViewModel>(items)
{
    public string Title { get; } = title;

    public Color Color { get; } = color;
}

/// <summary>The "All · Chest · Back…" chips above a list of exercises, and the search that goes with them.</summary>
public sealed class ExerciseFilter
{
    private readonly Action _changed;

    public ExerciseFilter(Action changed)
    {
        _changed = changed;
        Chips = new MuscleGroup?[] { null }
            .Concat(Enum.GetValues<MuscleGroup>().Cast<MuscleGroup?>())
            .Select((muscle, index) => new SelectableOption(muscle?.ToString() ?? string.Empty, index, int.MaxValue, Select)
            {
                Label = muscle is { } m ? Loc.Muscle(m) : Loc.Get("Library_All"),
            })
            .ToList();
        Chips[0].IsSelected = true;
    }

    public IReadOnlyList<SelectableOption> Chips { get; }

    public MuscleGroup? Muscle { get; private set; }

    public string Search { get; set; } = string.Empty;

    /// <summary>The exercises that match, grouped by muscle, in the catalog's order of muscle groups.</summary>
    public IReadOnlyList<ExerciseGroup> Apply(IEnumerable<ExerciseItemViewModel> exercises)
    {
        var words = TextSearch.Normalize(Search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var comparer = StringComparer.Create(Loc.Culture, CompareOptions.IgnoreCase);
        return exercises
            .Where(e => (Muscle is null || e.Muscle == Muscle) && words.All(w => e.SearchText.Contains(w, StringComparison.Ordinal)))
            .GroupBy(e => e.Muscle)
            .OrderBy(g => g.Key)
            .Select(g => new ExerciseGroup(Loc.Muscle(g.Key), Palette.Muscle(g.Key), g.OrderBy(e => e.Name, comparer)))
            .ToList();
    }

    private void Select(SelectableOption chip)
    {
        foreach (var option in Chips)
            option.IsSelected = option == chip;
        Muscle = Enum.TryParse<MuscleGroup>(chip.Value, out var muscle) ? muscle : null;
        Palette.Haptic();
        _changed();
    }
}

internal static class TextSearch
{
    /// <summary>Lower case without accents, so "developpe" finds "Développé couché".</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
