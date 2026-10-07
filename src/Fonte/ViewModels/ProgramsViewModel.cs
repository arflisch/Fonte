using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Catalog;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Core.Training;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>"Programs" tab: the program followed and its workouts, the user's own workouts, ready-made programs.</summary>
public sealed partial class ProgramsViewModel(
    FonteStore store, AppSettings settings, IDialogService dialogs, WorkoutLauncher launcher, ProService pro)
    : ReloadingViewModel
{
    private ProgramOverview? _active;

    [ObservableProperty]
    private bool _hasActive;

    [ObservableProperty]
    private string _activeName = string.Empty;

    [ObservableProperty]
    private string _activeWeekText = string.Empty;

    [ObservableProperty]
    private string _activePlanText = string.Empty;

    [ObservableProperty]
    private double _activeProgress;

    [ObservableProperty]
    private IReadOnlyList<TemplateItemViewModel> _activeTemplates = [];

    [ObservableProperty]
    private bool _activeIsEmpty;

    [ObservableProperty]
    private IReadOnlyList<ProgramRowViewModel> _otherPrograms = [];

    [ObservableProperty]
    private bool _hasOtherPrograms;

    [ObservableProperty]
    private IReadOnlyList<TemplateItemViewModel> _templates = [];

    [ObservableProperty]
    private bool _hasTemplates;

    [ObservableProperty]
    private IReadOnlyList<CatalogItemViewModel> _catalog = [];

    /// <summary>"PRO" next to creating a program, while Fonte Pro is locked.</summary>
    [ObservableProperty]
    private bool _isLocked;

    protected override async Task LoadCoreAsync()
    {
        var today = DateTime.Today;
        var programs = await store.GetProgramsAsync(today);
        _active = programs.FirstOrDefault(p => p.Program.IsActive);
        HasActive = _active is not null;
        if (_active is { } active)
        {
            ActiveName = active.Program.Name;
            ActiveWeekText = active.Program.Weeks > 0
                ? Loc.Format("Programs_Week", active.Week, active.Program.Weeks)
                : Loc.Get("Programs_Active");
            ActiveProgress = active.Program.Weeks > 0 ? (double)active.Week / active.Program.Weeks : 0;
            var perWeek = active.Program.CatalogKey is { } key && ProgramCatalog.All.FirstOrDefault(p => p.Key == key) is { } catalog
                ? catalog.SessionsPerWeek
                : settings.SessionsPerWeek;
            ActivePlanText = Loc.Format("Programs_PerWeek", perWeek);
            ActiveTemplates = active.Templates
                .Select((t, i) => new TemplateItemViewModel(t, Letter(i), t.Template.Id == active.Next?.Template.Id, OpenTemplateAsync, launcher.StartTemplateAsync))
                .ToList();
            ActiveIsEmpty = ActiveTemplates.Count == 0;
        }

        OtherPrograms = programs.Where(p => !p.Program.IsActive).Select(p => new ProgramRowViewModel(p, ProgramMenuAsync)).ToList();
        HasOtherPrograms = OtherPrograms.Count > 0;

        var templates = await store.GetStandaloneTemplatesAsync();
        Templates = templates.Select(t => new TemplateItemViewModel(t, null, false, OpenTemplateAsync, launcher.StartTemplateAsync)).ToList();
        HasTemplates = Templates.Count > 0;

        Catalog = ProgramCatalog.All.Select(p => new CatalogItemViewModel(p, FollowCatalogAsync)).ToList();
        IsLocked = !pro.IsUnlocked;
    }

    /// <summary>Free users can create a few workouts of their own; Fonte Pro has no limit.</summary>
    private async Task<bool> CanCreateTemplateAsync() =>
        pro.IsUnlocked
        || await store.CountOwnTemplatesAsync() < ProService.FreeOwnTemplates
        || await pro.EnsureUnlockedAsync(dialogs, Loc.Get("Programs_LimitTitle"), Loc.Format("Programs_LimitText", ProService.FreeOwnTemplates));

    private static string Letter(int index) => ((char)('A' + index % 26)).ToString();

    private Task OpenTemplateAsync(int templateId) => Shell.Current.GoToAsync($"{Routes.Template}?id={templateId}");

    private async Task FollowCatalogAsync(CatalogProgram catalog)
    {
        var name = Loc.Get($"Program_{catalog.Key}");
        var confirmed = await dialogs.ConfirmAsync(
            Loc.Format("Programs_FollowTitle", name),
            HasActive ? Loc.Format("Programs_FollowReplace", ActiveName) : Loc.Get("Programs_FollowText"),
            Loc.Get("Programs_Follow"));
        if (!confirmed)
            return;
        await store.CreateProgramFromCatalogAsync(ProgramCatalog.Adapt(catalog, settings.Goal), Loc.Get, DateTime.Now);
        Palette.Haptic();
        SuccessToast.Show(Loc.Format("Programs_Followed", name));
    }

    [RelayCommand]
    private async Task NewProgramAsync()
    {
        if (!await pro.EnsureUnlockedAsync(dialogs, Loc.Get("Programs_NewProgram"), Loc.Get("Programs_OwnProgramPro")))
            return;
        var name = await dialogs.PromptAsync(Loc.Get("Programs_NewProgram"), Loc.Get("Programs_NamePlaceholder"), string.Empty, 60);
        if (string.IsNullOrWhiteSpace(name))
            return;
        var program = await store.CreateProgramAsync(name, DateTime.Now);
        var template = await store.CreateTemplateAsync(program.Id, Loc.Format("Programs_DefaultTemplate", "A"), DateTime.Now);
        await OpenTemplateAsync(template.Id);
    }

    [RelayCommand]
    private async Task NewTemplateAsync()
    {
        if (!await CanCreateTemplateAsync())
            return;
        var name = await dialogs.PromptAsync(Loc.Get("Programs_NewTemplate"), Loc.Get("Programs_TemplatePlaceholder"), string.Empty, 60);
        if (string.IsNullOrWhiteSpace(name))
            return;
        var template = await store.CreateTemplateAsync(null, name, DateTime.Now);
        await OpenTemplateAsync(template.Id);
    }

    [RelayCommand]
    private async Task ActiveMenuAsync()
    {
        if (_active is not { } active)
            return;
        var add = Loc.Get("Programs_AddTemplate");
        var rename = Loc.Get("Programs_Rename");
        var restart = Loc.Get("Programs_Restart");
        var stop = Loc.Get("Programs_Stop");
        var delete = Loc.Get("Programs_Delete");
        var choice = await dialogs.ChooseAsync(active.Program.Name, delete, add, rename, restart, stop);
        if (choice == add)
        {
            if (!await CanCreateTemplateAsync())
                return;
            var letter = Letter(active.Templates.Count);
            var name = await dialogs.PromptAsync(add, Loc.Get("Programs_TemplatePlaceholder"), Loc.Format("Programs_DefaultTemplate", letter), 60);
            if (!string.IsNullOrWhiteSpace(name))
                await OpenTemplateAsync((await store.CreateTemplateAsync(active.Program.Id, name, DateTime.Now)).Id);
        }
        else if (choice == rename)
        {
            await RenameAsync(active.Program);
        }
        else if (choice == restart)
        {
            await store.ActivateProgramAsync(active.Program.Id, DateTime.Now);
            SuccessToast.Show(Loc.Get("Programs_Restarted"));
        }
        else if (choice == stop)
        {
            await store.StopProgramAsync(active.Program.Id);
        }
        else if (choice == delete)
        {
            await DeleteAsync(active.Program);
        }
    }

    private async Task ProgramMenuAsync(TrainingProgram program)
    {
        var follow = Loc.Get("Programs_Follow");
        var rename = Loc.Get("Programs_Rename");
        var delete = Loc.Get("Programs_Delete");
        var choice = await dialogs.ChooseAsync(program.Name, delete, follow, rename);
        if (choice == follow)
        {
            await store.ActivateProgramAsync(program.Id, DateTime.Now);
            Palette.Haptic();
        }
        else if (choice == rename)
        {
            await RenameAsync(program);
        }
        else if (choice == delete)
        {
            await DeleteAsync(program);
        }
    }

    private async Task RenameAsync(TrainingProgram program)
    {
        var name = await dialogs.PromptAsync(Loc.Get("Programs_Rename"), Loc.Get("Programs_NamePlaceholder"), program.Name, 60);
        if (!string.IsNullOrWhiteSpace(name))
            await store.RenameProgramAsync(program.Id, name);
    }

    private async Task DeleteAsync(TrainingProgram program)
    {
        var confirmed = await dialogs.ConfirmAsync(
            Loc.Format("Programs_DeleteTitle", program.Name), Loc.Get("Programs_DeleteText"), Loc.Get("Programs_Delete"));
        if (!confirmed)
            return;
        await store.DeleteProgramAsync(program.Id);
        Palette.Haptic();
    }
}

/// <summary>A workout template in a list: its letter in the program, what it works, when it was last done.</summary>
public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(TemplateOverview overview, string? letter, bool isNext, Func<int, Task> open, Func<int, Task> start)
    {
        var id = overview.Template.Id;
        Letter = letter ?? overview.Template.Name[..1].ToUpper(Loc.Culture);
        Name = overview.Template.Name;
        var muscles = overview.Muscles.Take(3).Select(Loc.Muscle).ToList();
        MusclesText = muscles.Count > 0 ? string.Join(", ", muscles) : Loc.Get("Programs_NoExercise");
        IsNext = isNext;
        var done = overview.LastDone is { } last ? Loc.Format("Programs_DoneOn", Loc.Day(last).ToLower(Loc.Culture)) : null;
        Caption = string.Join(" · ", new[]
        {
            Loc.Count(overview.Entries.Count, "Exercise"),
            isNext ? Loc.Get("Programs_Next") : done,
        }.OfType<string>());
        Color = overview.Muscles.Count > 0 ? Palette.Muscle(overview.Muscles[0]) : Palette.Primary;
        SoftColor = Palette.Soft(Color);
        CanStart = overview.Entries.Count > 0;
        OpenCommand = new AsyncRelayCommand(() => open(id));
        StartCommand = new AsyncRelayCommand(() => start(id));
    }

    public string Letter { get; }

    public string Name { get; }

    public string MusclesText { get; }

    public string Caption { get; }

    public bool IsNext { get; }

    public bool CanStart { get; }

    public Color Color { get; }

    public Color SoftColor { get; }

    public IAsyncRelayCommand OpenCommand { get; }

    public IAsyncRelayCommand StartCommand { get; }
}

/// <summary>A program that isn't followed right now.</summary>
public sealed class ProgramRowViewModel
{
    public ProgramRowViewModel(ProgramOverview overview, Func<TrainingProgram, Task> menu)
    {
        Name = overview.Program.Name;
        Caption = string.Join(" · ", new[]
        {
            Loc.Count(overview.Templates.Count, "Session"),
            overview.Program.Weeks > 0 ? Loc.Format("Programs_Weeks", overview.Program.Weeks) : null,
        }.OfType<string>());
        MenuCommand = new AsyncRelayCommand(() => menu(overview.Program));
    }

    public string Name { get; }

    public string Caption { get; }

    public IAsyncRelayCommand MenuCommand { get; }
}

/// <summary>A ready-made program offered to follow.</summary>
public sealed class CatalogItemViewModel
{
    public CatalogItemViewModel(CatalogProgram program, Func<CatalogProgram, Task> follow)
    {
        Name = Loc.Get($"Program_{program.Key}");
        Text = Loc.Get($"ProgramText_{program.Key}");
        Plan = Loc.Format("Programs_CatalogPlan", program.SessionsPerWeek, program.Weeks);
        Emoji = program.Key switch
        {
            "full_body" => "🌱",
            "five_by_five" => "🏋️",
            "upper_lower" => "⚖️",
            _ => "🔥",
        };
        FollowCommand = new AsyncRelayCommand(() => follow(program));
    }

    public string Name { get; }

    public string Text { get; }

    public string Plan { get; }

    public string Emoji { get; }

    public IAsyncRelayCommand FollowCommand { get; }
}
