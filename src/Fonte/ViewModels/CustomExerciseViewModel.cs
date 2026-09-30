using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>Sheet to create one of the user's own exercises, or to edit it.</summary>
public sealed partial class CustomExerciseViewModel : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private readonly FonteStore _store;
    private Exercise _exercise = new() { Muscle = MuscleGroup.Chest, Equipment = Equipment.Barbell, Tracking = Tracking.WeightAndReps };
    private bool _isSaving;

    public CustomExerciseViewModel(FonteStore store)
    {
        _store = store;
        Muscles = Options(Enum.GetValues<MuscleGroup>(), int.MaxValue, m => Loc.Muscle(m), SelectMuscle);
        Equipments = Options(Enum.GetValues<Equipment>(), int.MaxValue, e => Loc.Equipment(e), SelectEquipment);
        Trackings = Options(Enum.GetValues<Tracking>(), 3, t => Loc.Tracking(t), SelectTracking);
        ShowSelection();
    }

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public IReadOnlyList<SelectableOption> Muscles { get; }

    public IReadOnlyList<SelectableOption> Equipments { get; }

    public IReadOnlyList<SelectableOption> Trackings { get; }

    [ObservableProperty]
    private string _title = Loc.Get("Exercise_New");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var id) || !int.TryParse(id?.ToString(), out var exerciseId))
            return;
        if (await _store.GetExerciseAsync(exerciseId) is not { IsCustom: true } exercise)
            return;

        _exercise = exercise;
        Title = Loc.Get("Custom_TitleEdit");
        Name = exercise.CustomName ?? string.Empty;
        ShowSelection();
    }

    partial void OnNameChanged(string value) => HasError = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_isSaving)
            return;
        _isSaving = true;
        try
        {
            var isNew = _exercise.Id == 0;
            _exercise.CustomName = Name;
            var saved = await _store.SaveCustomExerciseAsync(_exercise);
            if (isNew)
                WeakReferenceMessenger.Default.Send(new ExerciseCreatedMessage(saved.Id));
            SuccessToast.Show(Loc.Get("Custom_Saved"));
            await Shell.Current.GoToAsync("..");
        }
        catch (FonteException ex)
        {
            ErrorMessage = Loc.Error(ex);
            HasError = true;
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");

    private void SelectMuscle(SelectableOption option)
    {
        _exercise.Muscle = Enum.Parse<MuscleGroup>(option.Value);
        // Cardio is timed: suggest it for a new exercise.
        if (_exercise.Id == 0 && _exercise.Muscle == MuscleGroup.Cardio)
            _exercise.Tracking = Tracking.Time;
        ShowSelection();
    }

    private void SelectEquipment(SelectableOption option)
    {
        _exercise.Equipment = Enum.Parse<Equipment>(option.Value);
        ShowSelection();
    }

    private void SelectTracking(SelectableOption option)
    {
        _exercise.Tracking = Enum.Parse<Tracking>(option.Value);
        ShowSelection();
    }

    private void ShowSelection()
    {
        foreach (var option in Muscles)
            option.IsSelected = option.Value == _exercise.Muscle.ToString();
        foreach (var option in Equipments)
            option.IsSelected = option.Value == _exercise.Equipment.ToString();
        foreach (var option in Trackings)
            option.IsSelected = option.Value == _exercise.Tracking.ToString();
    }

    private static IReadOnlyList<SelectableOption> Options<T>(
        IEnumerable<T> values, int columns, Func<T, string> label, Action<SelectableOption> select)
        where T : struct, Enum =>
        values.Select((v, i) => new SelectableOption(v.ToString(), i, columns, select) { Label = label(v) }).ToList();
}
