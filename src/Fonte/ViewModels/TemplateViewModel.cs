using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>A workout template: its exercises in order, what each aims for, supersets; and starting it.</summary>
public sealed partial class TemplateViewModel(
    FonteStore store, IDialogService dialogs, WorkoutLauncher launcher, ProService pro, AppSettings settings)
    : ReloadingViewModel, IQueryAttributable
{
    // Typed targets are saved one after the other, never out of order.
    private readonly SerialQueue _queue = new();
    private int _templateId;
    private bool _isGone;

    public ObservableCollection<TemplateExerciseViewModel> Exercises { get; } = [];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _canStart;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id) && int.TryParse(id?.ToString(), out var templateId))
        {
            _templateId = templateId;
            RequestReload();
        }
    }

    protected override async Task LoadCoreAsync()
    {
        if (_templateId == 0 || _isGone)
            return;
        await _queue.WhenIdle();

        TemplateOverview overview;
        try
        {
            overview = await store.GetTemplateAsync(_templateId);
        }
        catch (FonteException)
        {
            _isGone = true; // Deleted: the page is being closed.
            return;
        }

        Name = overview.Template.Name;
        Exercises.Clear();
        for (var i = 0; i < overview.Entries.Count; i++)
        {
            var linkedFromPrevious = i > 0 && overview.Entries[i - 1].Item.LinkedToNext;
            var isLast = i == overview.Entries.Count - 1;
            Exercises.Add(new TemplateExerciseViewModel(overview.Entries[i], linkedFromPrevious, isLast, this));
        }
        IsEmpty = Exercises.Count == 0;
        CanStart = !IsEmpty;
        UpdateSummary();
    }

    internal void UpdateSummary()
    {
        var sets = Exercises.Sum(e => e.SetCount);
        SummaryText = IsEmpty
            ? Loc.Get("Template_EmptyText")
            : $"{Loc.Count(Exercises.Count, "Exercise")} · {Loc.Count(sets, "Set")}";
    }

    internal void QueueSave(TemplateExerciseViewModel item)
    {
        if (item.IsSavePending)
            return;
        item.IsSavePending = true;
        _ = _queue.Enqueue(async () =>
        {
            item.IsSavePending = false;
            try
            {
                await store.UpdateTemplateExerciseAsync(item.ItemId, item.SetCount, item.Reps, item.Weight, item.Seconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Template exercise {item.ItemId} not saved: {ex}");
            }
        });
        UpdateSummary();
    }

    internal async Task ExerciseMenuAsync(TemplateExerciseViewModel item)
    {
        var index = Exercises.IndexOf(item);
        var up = Loc.Get("Template_MoveUp");
        var down = Loc.Get("Template_MoveDown");
        var link = item.IsLinkedToNext ? Loc.Get("Template_Unlink") : Loc.Get("Template_Link");
        var history = Loc.Get("Workout_History");
        var remove = Loc.Get("Template_Remove");
        var rest = pro.IsUnlocked ? Loc.Get("Workout_RestTime") : $"{Loc.Get("Workout_RestTime")} · PRO";
        var options = new List<string> { rest };
        if (index > 0)
            options.Add(up);
        if (index < Exercises.Count - 1)
        {
            options.Add(down);
            options.Add(link);
        }
        options.Add(history);

        var choice = await dialogs.ChooseAsync(item.Name, remove, [.. options]);
        await _queue.WhenIdle();
        if (choice == rest)
            await ChooseRestAsync(item);
        else if (choice == up || choice == down)
            await store.MoveTemplateExerciseAsync(item.ItemId, choice == up ? -1 : 1);
        else if (choice == link)
            await store.SetTemplateExerciseLinkAsync(item.ItemId, !item.IsLinkedToNext);
        else if (choice == history)
            await Shell.Current.GoToAsync($"{Routes.Exercise}?id={item.ExerciseId}");
        else if (choice == remove)
            await store.RemoveTemplateExerciseAsync(item.ItemId);
        if (choice is not null && choice != history)
            Palette.Haptic();
    }

    /// <summary>The rest after each set of this exercise (Fonte Pro).</summary>
    private async Task ChooseRestAsync(TemplateExerciseViewModel item)
    {
        if (!pro.IsUnlocked)
        {
            await Shell.Current.GoToAsync(Routes.Pro);
            return;
        }
        var standard = Loc.Format("Workout_RestDefault", Loc.Seconds(settings.RestSeconds));
        var options = new[] { standard }
            .Concat(AppSettings.RestChoices.Select(s => Loc.Seconds(s) + (s == item.RestSeconds ? "  ✓" : string.Empty)))
            .ToArray();
        var choice = await dialogs.ChooseAsync(Loc.Format("Workout_RestFor", item.Name), null, options);
        if (choice is null)
            return;
        var seconds = choice == standard ? 0 : AppSettings.RestChoices[Array.IndexOf(options, choice) - 1];
        await store.SetTemplateExerciseRestAsync(item.ItemId, seconds);
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task AddExerciseAsync() => Shell.Current.GoToAsync($"{Routes.PickExercises}?template={_templateId}");

    [RelayCommand]
    private async Task StartAsync()
    {
        await _queue.WhenIdle();
        await launcher.StartTemplateAsync(_templateId);
    }

    [RelayCommand]
    private async Task MenuAsync()
    {
        var rename = Loc.Get("Programs_Rename");
        var delete = Loc.Get("Template_Delete");
        var choice = await dialogs.ChooseAsync(Name, delete, rename);
        if (choice == rename)
        {
            var name = await dialogs.PromptAsync(rename, Loc.Get("Programs_TemplatePlaceholder"), Name, 60);
            if (!string.IsNullOrWhiteSpace(name))
                await store.RenameTemplateAsync(_templateId, name);
        }
        else if (choice == delete)
        {
            var confirmed = await dialogs.ConfirmAsync(Loc.Format("Programs_DeleteTitle", Name), Loc.Get("Template_DeleteText"), delete);
            if (!confirmed)
                return;
            _isGone = true;
            await store.DeleteTemplateAsync(_templateId);
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
        }
    }
}

/// <summary>An exercise of a template: sets, repetitions (or duration) and load to aim for.</summary>
public sealed partial class TemplateExerciseViewModel : ObservableObject
{
    private readonly TemplateViewModel _owner;
    private readonly Exercise _exercise;

    public TemplateExerciseViewModel(TemplateEntry entry, bool linkedFromPrevious, bool isLast, TemplateViewModel owner)
    {
        _owner = owner;
        _exercise = entry.Exercise;
        var item = entry.Item;
        ItemId = item.Id;
        ExerciseId = entry.Exercise.Id;
        Name = Loc.ExerciseName(entry.Exercise);
        RestSeconds = item.RestSeconds;
        MuscleText = $"{Loc.Muscle(entry.Exercise.Muscle)} · {Loc.Equipment(entry.Exercise.Equipment)}"
            + (item.RestSeconds > 0 ? $" · {Loc.Format("Workout_RestShort", Loc.Seconds(item.RestSeconds))}" : string.Empty);
        Color = Palette.Muscle(entry.Exercise.Muscle);
        IsTime = entry.Exercise.Tracking == Tracking.Time;
        ShowWeight = entry.Exercise.Tracking == Tracking.WeightAndReps;
        UsesMinutes = IsTime && entry.Exercise.Muscle == MuscleGroup.Cardio;
        ValueHeader = !IsTime
            ? Loc.Get("Workout_ColReps")
            : UsesMinutes ? Loc.Get("Workout_ColMinutes") : Loc.Get("Workout_ColSeconds");
        IsLinkedToNext = item.LinkedToNext && !isLast;
        IsLinkedFromPrevious = linkedFromPrevious;
        _setsText = Loc.Number(item.Sets);
        _valueText = IsTime
            ? item.Seconds > 0 ? Loc.Number(UsesMinutes ? Math.Round(item.Seconds / 60.0, 2) : item.Seconds) : string.Empty
            : item.Reps > 0 ? Loc.Number(item.Reps) : string.Empty;
        _weightText = item.Weight > 0 ? Loc.Number(item.Weight) : string.Empty;
        MenuCommand = new AsyncRelayCommand(() => owner.ExerciseMenuAsync(this));
    }

    public int ItemId { get; }

    public int ExerciseId { get; }

    /// <summary>The exercise's own rest; 0 for the rest of the settings.</summary>
    public int RestSeconds { get; }

    public string Name { get; }

    public string MuscleText { get; }

    public Color Color { get; }

    public bool IsTime { get; }

    public bool ShowWeight { get; }

    public bool UsesMinutes { get; }

    public string ValueHeader { get; }

    /// <summary>Done back to back with the next one: the superset badge goes under it.</summary>
    public bool IsLinkedToNext { get; }

    public bool IsLinkedFromPrevious { get; }

    /// <summary>The load is learnt from the first workout when it is left empty.</summary>
    public string WeightPlaceholder => Loc.Get("Template_WeightAuto");

    internal bool IsSavePending { get; set; }

    [ObservableProperty]
    private string _setsText;

    [ObservableProperty]
    private string _valueText;

    [ObservableProperty]
    private string _weightText;

    public int SetCount => Math.Clamp((int)Math.Round(Parse(SetsText)), 1, 20);

    public int Reps => IsTime ? 0 : (int)Math.Round(Parse(ValueText));

    public int Seconds => IsTime ? (int)Math.Round(Parse(ValueText) * (UsesMinutes ? 60 : 1)) : 0;

    public double Weight => ShowWeight ? Parse(WeightText) : 0;

    public IAsyncRelayCommand MenuCommand { get; }

    partial void OnSetsTextChanged(string value) => _owner.QueueSave(this);

    partial void OnValueTextChanged(string value) => _owner.QueueSave(this);

    partial void OnWeightTextChanged(string value) => _owner.QueueSave(this);

    private static double Parse(string? text) =>
        double.TryParse(text?.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0
            ? Math.Min(value, 10_000)
            : 0;
}
