using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>
/// Sheet to bring the history of Strong or Hevy into Fonte: how to export it there, then what the file holds
/// (workouts, sets, period, exercises to create) before anything is added.
/// </summary>
public sealed partial class ImportViewModel(FonteStore store, AppSettings settings, BackupFilePicker picker, IDialogService dialogs)
    : ObservableObject, ISheetViewModel
{
    private string? _text;
    private ImportFile? _file;
    private bool _isBusy;

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing))]
    private bool _hasPreview;

    public bool IsChoosing => !HasPreview;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private string _workoutsText = string.Empty;

    [ObservableProperty]
    private string _periodText = string.Empty;

    [ObservableProperty]
    private string _unitText = string.Empty;

    [ObservableProperty]
    private bool _canChangeUnit;

    [ObservableProperty]
    private string _exercisesText = string.Empty;

    [ObservableProperty]
    private bool _canImport;

    [ObservableProperty]
    private bool _isWorking;

    [RelayCommand]
    private async Task ChooseFileAsync()
    {
        if (_isBusy)
            return;
        _isBusy = IsWorking = true;
        try
        {
            if (await picker.OpenImportAsync() is not { } path)
                return;
            _text = await File.ReadAllTextAsync(path);
            File.Delete(path);
            _file = WorkoutImport.Read(_text, settings.UsesPounds);
            if (_file is null || _file.Workouts.Count == 0)
            {
                await dialogs.AlertAsync(Loc.Get("Import_UnknownTitle"), Loc.Get("Import_UnknownText"));
                return;
            }
            // Older Strong files don't say their unit: ask, offering the one the app uses first.
            if (!_file.UnitKnown)
                await AskUnitAsync();
            await PreviewAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await dialogs.AlertAsync(Loc.Get("Import_UnknownTitle"), Loc.Get("Import_UnknownText"));
        }
        finally
        {
            _isBusy = IsWorking = false;
        }
    }

    private async Task AskUnitAsync()
    {
        var kilograms = Loc.Get("Settings_UnitKilograms");
        var pounds = Loc.Get("Settings_UnitPounds");
        var choice = await dialogs.ChooseAsync(Loc.Get("Import_UnitQuestion"), null,
            settings.UsesPounds ? [pounds, kilograms] : [kilograms, pounds]);
        if (choice is not null && _text is not null)
            _file = WorkoutImport.Read(_text, choice == pounds);
    }

    private async Task PreviewAsync()
    {
        if (_file is null)
            return;
        var preview = await store.PreviewImportAsync(_file.Workouts);
        SourceText = Loc.Format("Import_From", _file.Source == ImportSource.Strong ? "Strong" : "Hevy");
        WorkoutsText = preview.NewWorkouts == preview.Workouts
            ? Loc.Format("Import_Workouts", Loc.Count(preview.NewWorkouts, "Workout"), Loc.Count(preview.Sets, "Set"))
            : Loc.Format("Import_WorkoutsSome", Loc.Count(preview.NewWorkouts, "Workout"), Loc.Count(preview.Sets, "Set"),
                preview.Workouts - preview.NewWorkouts);
        PeriodText = preview.From is { } from && preview.To is { } to
            ? Loc.Format("Import_Period", Loc.Date(from, "d MMM yyyy"), Loc.Date(to, "d MMM yyyy"))
            : string.Empty;
        UnitText = Loc.Format("Import_Unit", _file.WeightsInPounds ? Loc.Get("Unit_Pounds") : Loc.Get("Unit_Kilograms"));
        CanChangeUnit = !_file.UnitKnown;
        ExercisesText = preview.NewExercises.Count == 0
            ? Loc.Get("Import_AllExercisesKnown")
            : Loc.Format("Import_NewExercises", preview.NewExercises.Count, string.Join(", ", preview.NewExercises.Take(12))
                + (preview.NewExercises.Count > 12 ? "…" : string.Empty));
        CanImport = preview.NewWorkouts > 0;
        HasPreview = true;
    }

    [RelayCommand]
    private async Task ChangeUnitAsync()
    {
        await AskUnitAsync();
        await PreviewAsync();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_file is null || !CanImport || _isBusy)
            return;
        _isBusy = IsWorking = true;
        try
        {
            var result = await store.ImportAsync(_file.Workouts);
            Palette.Haptic();
            await dialogs.AlertAsync(Loc.Get("Import_DoneTitle"), Loc.Format("Import_DoneText",
                Loc.Count(result.Workouts, "Workout"), Loc.Count(result.Sets, "Set"), Loc.Count(result.NewExercises, "Exercise")));
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _isBusy = IsWorking = false;
        }
    }

    [RelayCommand]
    private void StartOver()
    {
        _file = null;
        _text = null;
        HasPreview = false;
    }

    [RelayCommand]
    private Task CloseAsync() => Shell.Current.GoToAsync("..");
}
