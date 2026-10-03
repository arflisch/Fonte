using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fonte.Core.Catalog;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>Sent when the user creates an exercise, so the picker that opened the form ticks it.</summary>
public sealed record ExerciseCreatedMessage(int ExerciseId);

/// <summary>
/// Sheet to add one or several exercises, in the order they are ticked, to the workout in progress or to a
/// workout template.
/// </summary>
public sealed partial class ExercisePickerViewModel : ReloadingViewModel, IQueryAttributable, ISheetViewModel
{
    private readonly FonteStore _store;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly List<int> _selected = [];
    private IReadOnlyList<ExerciseItemViewModel> _all = [];
    private int _workoutId;
    private int _templateId;
    private bool _isAdding;

    public ExercisePickerViewModel(FonteStore store, AppSettings settings, IDialogService dialogs)
    {
        _store = store;
        _settings = settings;
        _dialogs = dialogs;
        Filter = new ExerciseFilter(ApplyFilter);
        WeakReferenceMessenger.Default.Register<ExercisePickerViewModel, ExerciseCreatedMessage>(
            this, static (picker, message) => picker.OnExerciseCreated(message.ExerciseId));
    }

    public System.Windows.Input.ICommand DismissCommand => CancelCommand;

    public ExerciseFilter Filter { get; }

    [ObservableProperty]
    private IReadOnlyList<ExerciseGroup> _groups = [];

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private bool _hasNoResult;

    [ObservableProperty]
    private string _addText = string.Empty;

    [ObservableProperty]
    private bool _canAdd;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("workout", out var id) && int.TryParse(id?.ToString(), out var workoutId))
            _workoutId = workoutId;
        if (query.TryGetValue("template", out var template) && int.TryParse(template?.ToString(), out var templateId))
            _templateId = templateId;
        UpdateAddText();
        RequestReload();
    }

    partial void OnSearchChanged(string value)
    {
        Filter.Search = value;
        ApplyFilter();
    }

    protected override async Task LoadCoreAsync()
    {
        var overviews = await _store.GetExerciseOverviewsAsync();
        _all = overviews.Select(o => new ExerciseItemViewModel(o, Toggle) { IsSelected = _selected.Contains(o.Exercise.Id) }).ToList();
        ApplyFilter();
        UpdateAddText();
    }

    private void ApplyFilter()
    {
        Groups = Filter.Apply(_all);
        HasNoResult = _all.Count > 0 && Groups.Count == 0;
    }

    private void Toggle(ExerciseItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
        if (item.IsSelected)
            _selected.Add(item.Id);
        else
            _selected.Remove(item.Id);
        Palette.Haptic();
        UpdateAddText();
    }

    private void OnExerciseCreated(int exerciseId)
    {
        _selected.Add(exerciseId);
        UpdateAddText();
        RequestReload();
    }

    private void UpdateAddText()
    {
        CanAdd = _selected.Count > 0;
        AddText = CanAdd ? Loc.Format("Picker_Add", _selected.Count) : Loc.Get("Picker_AddNone");
    }

    [RelayCommand]
    private Task NewExerciseAsync() => Shell.Current.GoToAsync(Routes.EditExercise);

    [RelayCommand]
    private async Task AddAsync()
    {
        if (!CanAdd || _isAdding)
            return;
        _isAdding = true;
        try
        {
            foreach (var exerciseId in _selected)
            {
                if (_templateId != 0)
                    await _store.AddExerciseToTemplateAsync(_templateId, exerciseId, 3, ProgramCatalog.DefaultReps(_settings.Goal));
                else
                    await _store.AddExerciseToWorkoutAsync(_workoutId, exerciseId);
            }
            Palette.Haptic();
            await Shell.Current.GoToAsync("..");
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
        finally
        {
            _isAdding = false;
        }
    }

    [RelayCommand]
    private Task CancelAsync() => Shell.Current.GoToAsync("..");
}
