using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>The exercise library: every exercise with its record, searchable and filtered by muscle group.</summary>
public sealed partial class ExercisesViewModel : ReloadingViewModel
{
    private readonly FonteStore _store;
    private IReadOnlyList<ExerciseItemViewModel> _all = [];
    private string? _language;

    public ExercisesViewModel(FonteStore store)
    {
        _store = store;
        _filter = new ExerciseFilter(ApplyFilter);
    }

    [ObservableProperty]
    private ExerciseFilter _filter;

    [ObservableProperty]
    private IReadOnlyList<ExerciseGroup> _groups = [];

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private bool _hasNoResult;

    [ObservableProperty]
    private bool _isLoaded;

    partial void OnSearchChanged(string value)
    {
        Filter.Search = value;
        ApplyFilter();
    }

    protected override async Task LoadCoreAsync()
    {
        // The chips are labelled once: rebuild them when the language changes.
        if (_language != Localizer.Instance.Language.Code)
        {
            _language = Localizer.Instance.Language.Code;
            Filter = new ExerciseFilter(ApplyFilter) { Search = Search };
        }

        var overviews = await _store.GetExerciseOverviewsAsync();
        _all = overviews.Select(o => new ExerciseItemViewModel(o, Open)).ToList();
        ApplyFilter();
        IsLoaded = true;
    }

    private void ApplyFilter()
    {
        Groups = Filter.Apply(_all);
        HasNoResult = _all.Count > 0 && Groups.Count == 0;
    }

    private async void Open(ExerciseItemViewModel item) =>
        await Shell.Current.GoToAsync($"{Routes.Exercise}?id={item.Id}");

    [RelayCommand]
    private Task NewExerciseAsync() => Shell.Current.GoToAsync(Routes.EditExercise);
}
