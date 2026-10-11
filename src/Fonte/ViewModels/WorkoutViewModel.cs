using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>The workout in progress: its exercises and sets, the elapsed time and the rest countdown.</summary>
public sealed partial class WorkoutViewModel : ReloadingViewModel
{
    private const int RestStep = 15;
    private static readonly TimeSpan RestOverDisplay = TimeSpan.FromSeconds(4);

    private readonly FonteStore _store;
    private readonly AppSettings _settings;
    private readonly RestTimer _rest;
    private readonly IDialogService _dialogs;
    private readonly ProService _pro;
    private readonly HealthService _health;
    private readonly FinishedWorkouts _finished;

    // Every write goes through this queue, so a set is never ticked before its last typed value is saved.
    private readonly SerialQueue _queue = new();

    private IDispatcherTimer? _ticker;
    private Workout? _workout;
    private DateTime? _restOverUntil;
    private bool _isLeaving;
    private bool _permissionAsked;

    public WorkoutViewModel(
        FonteStore store, AppSettings settings, RestTimer rest, IDialogService dialogs, ProService pro,
        HealthService health, FinishedWorkouts finished)
    {
        _store = store;
        _settings = settings;
        _rest = rest;
        _dialogs = dialogs;
        _pro = pro;
        _health = health;
        _finished = finished;
    }

    internal bool IsPro => _pro.IsUnlocked;

    public ObservableCollection<WorkoutExerciseViewModel> Exercises { get; } = [];

    /// <summary>Raised when exercises were added, so the page can scroll down to them.</summary>
    public event EventHandler? ExercisesAdded;

    [ObservableProperty]
    private string _elapsedText = "0:00";

    /// <summary>The template's name for a planned workout.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isResting;

    [ObservableProperty]
    private bool _isRestOver;

    [ObservableProperty]
    private string _restText = string.Empty;

    [ObservableProperty]
    private double _restProgress;

    /// <summary>The page is on screen: start the clocks and keep the screen awake.</summary>
    public void Activate()
    {
        if (!IsLoaded)
            RequestReload();
        _ticker ??= CreateTicker();
        _ticker.Start();
        Tick();
        SetKeepScreenOn(true);

        if (!_permissionAsked && _settings.RestTimerEnabled)
        {
            _permissionAsked = true;
            _ = _rest.EnsurePermissionAsync();
        }
    }

    public void Deactivate()
    {
        _ticker?.Stop();
        SetKeepScreenOn(false);
    }

    protected override async Task LoadCoreAsync()
    {
        await _queue.WhenIdle();
        var workout = await _store.GetWorkoutInProgressAsync();
        if (workout is null)
        {
            // Finished or discarded: the page is closing.
            _workout = null;
            Exercises.Clear();
            return;
        }

        var detail = await _store.GetWorkoutDetailAsync(workout.Id);
        var added = IsLoaded && detail.Entries.Count > Exercises.Count;
        _workout = workout;
        Title = await TemplateNameAsync(workout) ?? Loc.Get("Workout_Title");
        Exercises.Clear();
        for (var i = 0; i < detail.Entries.Count; i++)
        {
            var linkedFromPrevious = i > 0 && detail.Entries[i - 1].Item.LinkedToNext;
            Exercises.Add(new WorkoutExerciseViewModel(detail.Entries[i], linkedFromPrevious, i == detail.Entries.Count - 1, this));
        }
        IsEmpty = Exercises.Count == 0;
        IsLoaded = true;
        Tick();

        if (added)
            ExercisesAdded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves a row shortly after it was typed in; several quick edits are written once.</summary>
    internal void QueueSave(SetRowViewModel row)
    {
        if (row.IsSavePending)
            return;
        row.IsSavePending = true;
        _ = _queue.Enqueue(async () =>
        {
            row.IsSavePending = false;
            try
            {
                await _store.UpdateSetAsync(row.Id, row.Weight, row.Reps, row.Seconds);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Set {row.Id} not saved: {ex}");
            }
        });
    }

    internal async Task ToggleAsync(SetRowViewModel row)
    {
        var done = !row.IsDone;
        try
        {
            await _queue.Enqueue(async () =>
            {
                await _store.UpdateSetAsync(row.Id, row.Weight, row.Reps, row.Seconds);
                await _store.SetDoneAsync(row.Id, done, DateTime.Now);
            });
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
            return;
        }

        row.IsDone = done;
        Palette.Haptic();
        if (done)
            row.Exercise.PrefillAfter(row);
        // In a superset, the rest comes after the last exercise of the chain.
        if (done && _settings.RestTimerEnabled && !row.Exercise.IsLinkedToNext)
        {
            _rest.Start(row.Exercise.RestSeconds ?? _settings.RestSeconds);
            _restOverUntil = null;
            Tick();
        }
    }

    internal async Task AddSetAsync(WorkoutExerciseViewModel exercise)
    {
        try
        {
            var set = await _queue.Enqueue(() => _store.AddSetAsync(exercise.ItemId));
            exercise.Sets.Add(new SetRowViewModel(set, exercise, this));
            exercise.Renumber();
            Palette.Haptic();
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }

    internal async Task SetMenuAsync(SetRowViewModel row)
    {
        var delete = Loc.Get("Workout_DeleteSet");
        var kind = ProLabel(Loc.Get("Workout_SetKind"));
        var rpe = ProLabel(Loc.Get("Workout_SetRpe"));
        var choice = await _dialogs.ChooseAsync(Loc.Format("Workout_SetNumber", row.NumberText), delete, kind, rpe);
        if (choice == kind)
        {
            await ChooseSetKindAsync(row);
            return;
        }
        if (choice == rpe)
        {
            await ChooseRpeAsync(row);
            return;
        }
        if (choice != delete)
            return;

        try
        {
            await _queue.Enqueue(() => _store.DeleteSetAsync(row.Id));
            row.Exercise.Sets.Remove(row);
            row.Exercise.Renumber();
            Palette.Haptic();
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }

    /// <summary>Marks the options of the menus that need Fonte Pro, while it is locked.</summary>
    private string ProLabel(string option) => _pro.IsUnlocked ? option : $"{option} · PRO";

    /// <summary>Opens the Fonte Pro page when it is locked; true when the feature can be used.</summary>
    private async Task<bool> OpenProUnlessUnlockedAsync()
    {
        if (_pro.IsUnlocked)
            return true;
        await Shell.Current.GoToAsync(Routes.Pro);
        return false;
    }

    private async Task ChooseSetKindAsync(SetRowViewModel row)
    {
        if (!await OpenProUnlessUnlockedAsync())
            return;
        var kinds = Enum.GetValues<SetKind>();
        var options = kinds.Select(k => SetRowViewModel.KindOption(k) + (k == row.Kind ? "  ✓" : string.Empty)).ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Get("Workout_SetKind"), null, options);
        if (choice is null)
            return;
        var kind = kinds[Array.IndexOf(options, choice)];
        await _queue.Enqueue(() => _store.SetSetKindAsync(row.Id, kind));
        row.Kind = kind;
        row.Exercise.Renumber();
        Palette.Haptic();
    }

    private async Task ChooseRpeAsync(SetRowViewModel row)
    {
        if (!await OpenProUnlessUnlockedAsync())
            return;
        double[] values = [6, 6.5, 7, 7.5, 8, 8.5, 9, 9.5, 10];
        var options = values.Select(v => Loc.Number(v) + (v == row.Rpe ? "  ✓" : string.Empty)).ToArray();
        var clear = Loc.Get("Workout_RpeClear");
        var choice = await _dialogs.ChooseAsync(Loc.Get("Workout_RpeTitle"), row.Rpe is null ? null : clear, options);
        if (choice is null)
            return;
        double? rpe = choice == clear ? null : values[Array.IndexOf(options, choice)];
        try
        {
            await _queue.Enqueue(() => _store.SetSetRpeAsync(row.Id, rpe));
            row.Rpe = rpe;
            Palette.Haptic();
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }

    private async Task ChooseRestAsync(WorkoutExerciseViewModel exercise)
    {
        if (!await OpenProUnlessUnlockedAsync())
            return;
        var standard = Loc.Format("Workout_RestDefault", Loc.Seconds(_settings.RestSeconds));
        var options = new[] { standard }
            .Concat(AppSettings.RestChoices.Select(s => Loc.Seconds(s) + (s == exercise.RestSeconds ? "  ✓" : string.Empty)))
            .ToArray();
        var choice = await _dialogs.ChooseAsync(Loc.Format("Workout_RestFor", exercise.Name), null, options);
        if (choice is null)
            return;
        int? seconds = choice == standard ? null : AppSettings.RestChoices[Array.IndexOf(options, choice) - 1];
        await _queue.Enqueue(() => _store.SetWorkoutExerciseRestAsync(exercise.ItemId, seconds));
        Palette.Haptic();
        RequestReload();
    }

    internal async Task ExerciseMenuAsync(WorkoutExerciseViewModel exercise)
    {
        var index = Exercises.IndexOf(exercise);
        var plates = ProLabel(Loc.Get("Workout_Plates"));
        var rest = ProLabel(Loc.Get("Workout_RestTime"));
        var up = Loc.Get("Template_MoveUp");
        var down = Loc.Get("Template_MoveDown");
        var link = exercise.IsLinkedToNext ? Loc.Get("Template_Unlink") : Loc.Get("Template_Link");
        var history = Loc.Get("Workout_History");
        var remove = Loc.Get("Workout_RemoveExercise");
        var options = new List<string>();
        options.Add(rest);
        if (exercise.Exercise.Equipment == Equipment.Barbell && exercise.ShowWeight)
            options.Add(plates);
        if (index > 0)
            options.Add(up);
        if (index >= 0 && index < Exercises.Count - 1)
        {
            options.Add(down);
            options.Add(link);
        }
        options.Add(history);

        var choice = await _dialogs.ChooseAsync(exercise.Name, remove, [.. options]);
        if (choice == rest)
        {
            await ChooseRestAsync(exercise);
        }
        else if (choice == plates)
        {
            if (!await OpenProUnlessUnlockedAsync())
                return;
            var weight = exercise.Sets.FirstOrDefault(s => !s.IsDone)?.Weight ?? exercise.Sets.LastOrDefault()?.Weight ?? 0;
            await Shell.Current.GoToAsync($"{Routes.Plates}?weight={weight.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        else if (choice == up || choice == down)
        {
            await _queue.Enqueue(() => _store.MoveWorkoutExerciseAsync(exercise.ItemId, choice == up ? -1 : 1));
            Palette.Haptic();
        }
        else if (choice == link)
        {
            await _queue.Enqueue(() => _store.SetWorkoutExerciseLinkAsync(exercise.ItemId, !exercise.IsLinkedToNext));
            Palette.Haptic();
        }
        else if (choice == history)
        {
            await Shell.Current.GoToAsync($"{Routes.Exercise}?id={exercise.Exercise.Id}");
        }
        else if (choice == remove)
        {
            await _queue.Enqueue(() => _store.RemoveExerciseFromWorkoutAsync(exercise.ItemId));
            Exercises.Remove(exercise);
            IsEmpty = Exercises.Count == 0;
            Palette.Haptic();
        }
    }

    private async Task<string?> TemplateNameAsync(Workout workout)
    {
        if (workout.TemplateId is not { } templateId)
            return null;
        try
        {
            return (await _store.GetTemplateAsync(templateId)).Template.Name;
        }
        catch (FonteException)
        {
            return null; // The template was deleted since.
        }
    }

    [RelayCommand]
    private Task GoBackAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task AddExerciseAsync()
    {
        if (_workout is { } workout)
            await Shell.Current.GoToAsync($"{Routes.PickExercises}?workout={workout.Id}");
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (_workout is not { } workout || _isLeaving)
            return;
        await _queue.WhenIdle();

        var sets = Exercises.SelectMany(e => e.Sets).ToList();
        if (!sets.Any(s => s.IsDone))
        {
            var discard = await _dialogs.ConfirmAsync(
                Loc.Get("Workout_NothingDoneTitle"),
                Loc.Get("Workout_NothingDoneText"),
                Loc.Get("Workout_DiscardConfirm"),
                Loc.Get("Workout_KeepGoing"));
            if (discard)
                await LeaveAsync(() => _store.DeleteWorkoutAsync(workout.Id), "..");
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Loc.Get("Workout_FinishTitle"),
            sets.All(s => s.IsDone) ? string.Empty : Loc.Get("Workout_FinishText"),
            Loc.Get("Workout_Finish"),
            Loc.Get("Workout_KeepGoing"));
        if (confirmed)
            await LeaveAsync(() => FinishWorkoutAsync(workout), $"../{Routes.Summary}?id={workout.Id}&fresh=1");
    }

    /// <summary>Automatic progression raises the template's targets with Fonte Pro; otherwise it is only shown.</summary>
    private async Task FinishWorkoutAsync(Workout workout)
    {
        if (await _store.FinishWorkoutAsync(workout.Id, DateTime.Now, applyProgression: _pro.IsUnlocked) is not { } summary)
            return;
        _finished.Remember(summary);
        _ = _health.SaveWorkoutAsync(summary.Workout.StartedAt, summary.Workout.FinishedAt ?? DateTime.Now);
    }

    [RelayCommand]
    private async Task DiscardAsync()
    {
        if (_workout is not { } workout || _isLeaving)
            return;
        var confirmed = await _dialogs.ConfirmAsync(
            Loc.Get("Workout_DiscardTitle"),
            Loc.Get("Workout_DiscardText"),
            Loc.Get("Workout_DiscardConfirm"),
            Loc.Get("Workout_KeepGoing"));
        if (confirmed)
            await LeaveAsync(() => _store.DeleteWorkoutAsync(workout.Id), "..");
    }

    [RelayCommand]
    private void AddRest()
    {
        _rest.Adjust(RestStep);
        Tick();
    }

    [RelayCommand]
    private void RemoveRest()
    {
        _rest.Adjust(-RestStep);
        Tick();
    }

    [RelayCommand]
    private void SkipRest()
    {
        _rest.Stop();
        _restOverUntil = null;
        Palette.Haptic();
        Tick();
    }

    /// <summary>Ends the workout (finished or discarded) and leaves the page.</summary>
    private async Task LeaveAsync(Func<Task> end, string route)
    {
        _isLeaving = true;
        try
        {
            await _queue.Enqueue(end);
            _rest.Stop();
            _workout = null;
            Palette.Haptic();
            await Shell.Current.GoToAsync(route);
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
        finally
        {
            _isLeaving = false;
        }
    }

    private void Tick()
    {
        var now = DateTime.Now;
        if (_workout is not null)
            ElapsedText = Loc.Clock(now - _workout.StartedAt);

        if (_rest.EndsAt is { } end)
        {
            if (end > now)
            {
                RestText = Loc.Clock(end - now);
                RestProgress = _rest.Progress;
                IsResting = true;
                IsRestOver = false;
                return;
            }

            // The rest just ended, or ended while the app was in the background (the notification rang then).
            _rest.Stop();
            if (now - end < TimeSpan.FromSeconds(3))
                Palette.Vibrate();
            if (now - end < TimeSpan.FromMinutes(1))
                _restOverUntil = now + RestOverDisplay;
        }

        IsResting = false;
        IsRestOver = _restOverUntil > now;
    }

    private IDispatcherTimer CreateTicker()
    {
        var timer = Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Tick();
        return timer;
    }

    private static void SetKeepScreenOn(bool keepOn)
    {
        try
        {
            DeviceDisplay.Current.KeepScreenOn = keepOn;
        }
        catch (Exception)
        {
            // Not every device lets an app keep its screen on.
        }
    }
}

/// <summary>An exercise of the workout in progress, with its sets.</summary>
public sealed class WorkoutExerciseViewModel
{
    private readonly IReadOnlyList<WorkoutSet> _previous;

    public WorkoutExerciseViewModel(Core.Training.WorkoutEntry entry, bool linkedFromPrevious, bool isLast, WorkoutViewModel owner)
    {
        var exercise = entry.Exercise;
        IsLinkedToNext = entry.Item.LinkedToNext && !isLast;
        IsLinkedFromPrevious = linkedFromPrevious;
        TargetText = Target(exercise, entry.Item);
        RestSeconds = entry.Item.RestSeconds;
        RestText = RestSeconds is { } rest ? Loc.Format("Workout_RestShort", Loc.Seconds(rest)) : string.Empty;
        ItemId = entry.Item.Id;
        Exercise = exercise;
        _previous = entry.PreviousSets;
        Name = Loc.ExerciseName(exercise);
        MuscleText = $"{Loc.Muscle(exercise.Muscle)} · {Loc.Equipment(exercise.Equipment)}";
        Color = Palette.Muscle(exercise.Muscle);
        ShowWeight = exercise.Tracking == Tracking.WeightAndReps;
        // Cardio lasts minutes, a plank seconds.
        UsesMinutes = exercise.Tracking == Tracking.Time && exercise.Muscle == MuscleGroup.Cardio;
        ValueHeader = exercise.Tracking != Tracking.Time
            ? Loc.Get("Workout_ColReps")
            : UsesMinutes ? Loc.Get("Workout_ColMinutes") : Loc.Get("Workout_ColSeconds");
        PreviousSpan = ShowWeight ? 1 : 2;
        Sets = new(entry.Sets.Select(s => new SetRowViewModel(s, this, owner)));
        Renumber();
        AddSetCommand = new AsyncRelayCommand(() => owner.AddSetAsync(this));
        MenuCommand = new AsyncRelayCommand(() => owner.ExerciseMenuAsync(this));
    }

    public int ItemId { get; }

    public Exercise Exercise { get; }

    /// <summary>Done back to back with the next exercise: no rest after its sets.</summary>
    public bool IsLinkedToNext { get; }

    public bool IsLinkedFromPrevious { get; }

    /// <summary>What the template aims for: "Target 3 × 8 · 45 kg".</summary>
    public string TargetText { get; }

    public bool HasTarget => TargetText.Length > 0;

    /// <summary>The exercise's own rest (Fonte Pro); null for the rest of the settings.</summary>
    public int? RestSeconds { get; }

    public string RestText { get; }

    public bool HasRest => RestText.Length > 0;

    public string Name { get; }

    public string MuscleText { get; }

    public Color Color { get; }

    public bool ShowWeight { get; }

    public bool UsesMinutes { get; }

    public string ValueHeader { get; }

    /// <summary>Without a load column, the "previous" column takes its room.</summary>
    public int PreviousSpan { get; }

    public ObservableCollection<SetRowViewModel> Sets { get; }

    public IAsyncRelayCommand AddSetCommand { get; }

    public IAsyncRelayCommand MenuCommand { get; }

    private static string Target(Exercise exercise, WorkoutExercise item)
    {
        if (item.TargetSets is not { } sets)
            return string.Empty;
        var each = exercise.Tracking == Tracking.Time
            ? item.TargetSeconds is { } seconds ? Loc.Seconds(seconds) : null
            : item.TargetReps?.ToString(Loc.Culture);
        var plan = each is null ? Loc.Count(sets, "Set") : $"{sets} × {each}";
        if (exercise.Tracking == Tracking.WeightAndReps && item.TargetWeight is { } weight)
            plan += $" · {Loc.Weight(weight)}";
        return Loc.Format("Workout_Target", plan);
    }

    /// <summary>The set after <paramref name="done"/>, when still blank, gets the same load and repetitions.</summary>
    public void PrefillAfter(SetRowViewModel done)
    {
        var index = Sets.IndexOf(done);
        if (index < 0 || index + 1 >= Sets.Count)
            return;
        var next = Sets[index + 1];
        if (next.IsDone || next.WeightText.Length > 0 || next.ValueText.Length > 0)
            return;
        next.WeightText = done.WeightText;
        next.ValueText = done.ValueText;
    }

    /// <summary>Numbers the sets and shows, next to each, the set done at the same place last time.</summary>
    public void Renumber()
    {
        // Warm-ups are not numbered: "W 1 2 3".
        var number = 0;
        for (var i = 0; i < Sets.Count; i++)
        {
            if (Sets[i].Kind != SetKind.WarmUp)
                number++;
            Sets[i].Number = number;
            Sets[i].PreviousText = i < _previous.Count ? Loc.SetShort(Exercise, _previous[i]) : "—";
        }
    }
}

/// <summary>One set of the workout in progress: what was lifted, and whether it was done.</summary>
public sealed partial class SetRowViewModel : ObservableObject
{
    private readonly WorkoutViewModel _owner;

    public SetRowViewModel(WorkoutSet set, WorkoutExerciseViewModel exercise, WorkoutViewModel owner)
    {
        _owner = owner;
        Id = set.Id;
        Exercise = exercise;
        _isDone = set.IsDone;
        _kind = set.Kind;
        _rpe = set.Rpe;
        _weightText = set.Weight > 0 ? Loc.Number(set.Weight) : string.Empty;
        _valueText = exercise.Exercise.Tracking switch
        {
            Tracking.Time when set.Seconds <= 0 => string.Empty,
            Tracking.Time => exercise.UsesMinutes ? Loc.Number(Math.Round(set.Seconds / 60.0, 2)) : Loc.Number(set.Seconds),
            _ => set.Reps > 0 ? Loc.Number(set.Reps) : string.Empty,
        };
        ToggleCommand = new AsyncRelayCommand(() => owner.ToggleAsync(this));
        MenuCommand = new AsyncRelayCommand(() => owner.SetMenuAsync(this));
    }

    public int Id { get; }

    public WorkoutExerciseViewModel Exercise { get; }

    public bool ShowWeight => Exercise.ShowWeight;

    public int PreviousSpan => Exercise.PreviousSpan;

    /// <summary>A save of this row waits in the queue: later edits are written by it.</summary>
    internal bool IsSavePending { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberText))]
    private int _number;

    /// <summary>Warm-up, drop set, to failure (Fonte Pro).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberText), nameof(KindColor), nameof(HasKind))]
    private SetKind _kind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RpeText), nameof(HasRpe))]
    private double? _rpe;

    /// <summary>The set's number, or its kind's letter ("W", "D", "F").</summary>
    public string NumberText => Kind == SetKind.Normal ? Number.ToString(Loc.Culture) : KindLetter(Kind);

    public bool HasKind => Kind != SetKind.Normal;

    public Color KindColor => Kind switch
    {
        SetKind.WarmUp => Color.FromArgb("#BA7517"),
        SetKind.Drop => Color.FromArgb("#D4537E"),
        SetKind.Failure => Color.FromArgb("#E24B4A"),
        _ => Colors.Transparent,
    };

    public string RpeText => Rpe is { } rpe ? $"@{Loc.Number(rpe)}" : string.Empty;

    public bool HasRpe => Rpe is not null;

    public static string KindLetter(SetKind kind) => kind switch
    {
        SetKind.WarmUp => Loc.Get("SetKind_WarmUpLetter"),
        SetKind.Drop => Loc.Get("SetKind_DropLetter"),
        SetKind.Failure => Loc.Get("SetKind_FailureLetter"),
        _ => string.Empty,
    };

    public static string KindName(SetKind kind) => kind switch
    {
        SetKind.WarmUp => Loc.Get("SetKind_WarmUp"),
        SetKind.Drop => Loc.Get("SetKind_Drop"),
        SetKind.Failure => Loc.Get("SetKind_Failure"),
        _ => Loc.Get("SetKind_Normal"),
    };

    /// <summary>"W — Warm-up" in the menu that changes it.</summary>
    public static string KindOption(SetKind kind) =>
        kind == SetKind.Normal ? KindName(kind) : $"{KindLetter(kind)} — {KindName(kind)}";

    [ObservableProperty]
    private string _previousText = string.Empty;

    [ObservableProperty]
    private string _weightText;

    /// <summary>Repetitions, or the duration (seconds, minutes for cardio).</summary>
    [ObservableProperty]
    private string _valueText;

    [ObservableProperty]
    private bool _isDone;

    public double Weight => ShowWeight ? Parse(WeightText) : 0;

    public int Reps => Exercise.Exercise.Tracking == Tracking.Time ? 0 : (int)Math.Round(Parse(ValueText));

    public int Seconds => Exercise.Exercise.Tracking != Tracking.Time
        ? 0
        : (int)Math.Round(Parse(ValueText) * (Exercise.UsesMinutes ? 60 : 1));

    public IAsyncRelayCommand ToggleCommand { get; }

    public IAsyncRelayCommand MenuCommand { get; }

    partial void OnWeightTextChanged(string value) => _owner.QueueSave(this);

    partial void OnValueTextChanged(string value) => _owner.QueueSave(this);

    /// <summary>Accepts "62.5" as well as "62,5": number pads don't always offer the language's separator.</summary>
    private static double Parse(string? text) =>
        double.TryParse(text?.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value > 0
            ? Math.Min(value, 10_000)
            : 0;
}
