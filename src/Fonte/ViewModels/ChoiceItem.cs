using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fonte.ViewModels;

/// <summary>A large option card: an emoji, a title and a line of explanation (training goal…).</summary>
public sealed partial class ChoiceItem(string value, string emoji, string title, string text, Action<ChoiceItem> onSelect)
    : ObservableObject
{
    public string Value { get; } = value;

    public string Emoji { get; } = emoji;

    public string Title { get; } = title;

    public string Text { get; } = text;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => onSelect(this);

    /// <summary>The cards for the training goals, in the order they are offered.</summary>
    public static IReadOnlyList<ChoiceItem> Goals(Action<ChoiceItem> onSelect) =>
    [
        new(nameof(Core.Catalog.TrainingGoal.Strength), "🏋️", Localization.Loc.Get("Goal_Strength"), Localization.Loc.Get("GoalText_Strength"), onSelect),
        new(nameof(Core.Catalog.TrainingGoal.Muscle), "💪", Localization.Loc.Get("Goal_Muscle"), Localization.Loc.Get("GoalText_Muscle"), onSelect),
        new(nameof(Core.Catalog.TrainingGoal.Fitness), "🏃", Localization.Loc.Get("Goal_Fitness"), Localization.Loc.Get("GoalText_Fitness"), onSelect),
    ];
}
