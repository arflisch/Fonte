using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>Sent when the app goes to the background: the progress photos lock again.</summary>
public sealed record AppBackgroundedMessage;

/// <summary>"Body" tab: body weight towards a goal, measurements, and progress photos kept behind Face ID.</summary>
public sealed partial class BodyViewModel : ReloadingViewModel
{
    private const int RecentWeighings = 5;

    private readonly FonteStore _store;
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogs;
    private readonly DeviceAuthentication _authentication;
    private readonly PhotoStore _photos;
    private IReadOnlyList<BodyWeight> _weights = [];
    private bool _isUnlocked;

    public BodyViewModel(FonteStore store, AppSettings settings, IDialogService dialogs, DeviceAuthentication authentication, PhotoStore photos)
    {
        _store = store;
        _settings = settings;
        _dialogs = dialogs;
        _authentication = authentication;
        _photos = photos;
        WeakReferenceMessenger.Default.Register<BodyViewModel, AppBackgroundedMessage>(this, static (vm, _) => vm.Lock());
    }

    [ObservableProperty]
    private string _weightText = "—";

    [ObservableProperty]
    private string _weightChangeText = string.Empty;

    [ObservableProperty]
    private string _goalText = string.Empty;

    [ObservableProperty]
    private double _goalProgress;

    [ObservableProperty]
    private bool _hasGoal;

    [ObservableProperty]
    private IReadOnlyList<double> _weightPoints = [];

    [ObservableProperty]
    private bool _hasWeights;

    [ObservableProperty]
    private bool _hasChart;

    [ObservableProperty]
    private IReadOnlyList<WeighingItem> _weighings = [];

    [ObservableProperty]
    private IReadOnlyList<MeasurementItem> _measurements = [];

    [ObservableProperty]
    private IReadOnlyList<PhotoItem> _photoItems = [];

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private bool _hasPhotos;

    [ObservableProperty]
    private bool _canCompare;

    [ObservableProperty]
    private string _unlockText = string.Empty;

    [ObservableProperty]
    private string _photosHint = string.Empty;

    protected override async Task LoadCoreAsync()
    {
        _weights = await _store.GetWeightsAsync();
        ShowWeights();

        var measurements = await _store.GetMeasurementsAsync();
        Measurements = Enum.GetValues<MeasurementKind>()
            .Select(kind =>
            {
                var values = measurements.Where(m => m.Kind == kind).ToList();
                return new MeasurementItem(kind, values.LastOrDefault(), values.Count > 1 ? values[^2] : null, EditMeasurementAsync);
            })
            .ToList();

        var biometry = _authentication.BiometryName;
        var lockable = _authentication.IsAvailable;
        IsLocked = lockable && !_isUnlocked;
        UnlockText = Loc.Format("Body_Unlock", biometry ?? Loc.Get("Body_Passcode"));
        PhotosHint = lockable ? Loc.Format("Body_PhotosLocked", biometry ?? Loc.Get("Body_Passcode")) : Loc.Get("Body_PhotosPrivate");
        var photos = await _store.GetPhotosAsync();
        PhotoItems = IsLocked ? [] : photos.Select(p => new PhotoItem(p, OpenPhotoAsync)).ToList();
        HasPhotos = photos.Count > 0;
        CanCompare = !IsLocked && photos.Count >= 2;
    }

    private void ShowWeights()
    {
        HasWeights = _weights.Count > 0;
        var goal = _settings.GoalWeight;
        HasGoal = goal > 0;
        if (_weights.Count == 0)
        {
            WeightText = "—";
            WeightChangeText = Loc.Get("Body_NoWeight");
            GoalText = HasGoal ? Loc.Format("Body_Goal", Loc.Weight(goal)) : Loc.Get("Body_SetGoal");
            WeightPoints = [];
            HasChart = false;
            Weighings = [];
            return;
        }

        var last = _weights[^1];
        WeightText = Loc.Weight(last.Kilograms);
        // Compared with eight weeks ago, or with the first weighing when they are more recent.
        var since = _weights.FirstOrDefault(w => w.Date >= last.Date.AddDays(-56)) ?? _weights[0];
        var change = Math.Round(last.Kilograms - since.Kilograms, 1);
        var weeks = Math.Max(1, (int)Math.Round((last.Date - since.Date).TotalDays / 7));
        WeightChangeText = since == last
            ? Loc.Day(last.Date)
            : Loc.Format("Body_Change", Signed(change, "kg"), Loc.Count(weeks, "Week"));
        if (HasGoal)
        {
            var left = Math.Round(goal - last.Kilograms, 1);
            GoalText = Math.Abs(left) < 0.05
                ? Loc.Format("Body_GoalReached", Loc.Weight(goal))
                : Loc.Format("Body_GoalLeft", Loc.Weight(goal), Signed(left, "kg"));
            var start = _weights[0].Kilograms;
            GoalProgress = Math.Abs(goal - start) < 0.05 ? 1 : Math.Clamp((last.Kilograms - start) / (goal - start), 0, 1);
        }
        else
        {
            GoalText = Loc.Get("Body_SetGoal");
        }

        // The last three months, one point per weighing.
        WeightPoints = _weights.Where(w => w.Date >= last.Date.AddDays(-90)).Select(w => w.Kilograms).ToList();
        HasChart = WeightPoints.Count >= 2;
        Weighings = _weights.Reverse().Take(RecentWeighings).Select(w => new WeighingItem(w, DeleteWeighingAsync)).ToList();
    }

    /// <summary>"+1.5 kg", "−3 cm", "=".</summary>
    internal static string Signed(double value, string unit) =>
        Math.Abs(value) < 0.05 ? "=" : $"{(value > 0 ? "+" : "−")}{Loc.Number(Math.Abs(value))} {unit}";

    internal void Lock()
    {
        if (!_isUnlocked)
            return;
        _isUnlocked = false;
        RequestReload();
    }

    [RelayCommand]
    private async Task AddWeightAsync()
    {
        var last = _weights.Count > 0 ? _weights[^1].Kilograms : (double?)null;
        if (await _dialogs.PromptNumberAsync(Loc.Get("Body_AddWeight"), Loc.Get("Body_AddWeightText"), last) is not { } kilograms)
            return;
        try
        {
            await _store.SaveWeightAsync(DateTime.Today, kilograms);
            Palette.Haptic();
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }

    [RelayCommand]
    private async Task SetGoalAsync()
    {
        var current = _settings.GoalWeight;
        var value = await _dialogs.PromptNumberAsync(Loc.Get("Body_GoalTitle"), Loc.Get("Body_GoalText"), current > 0 ? current : null);
        if (value is { } goal && goal is >= 20 and <= 400)
        {
            _settings.GoalWeight = goal;
            ShowWeights();
        }
    }

    private async Task DeleteWeighingAsync(BodyWeight weight)
    {
        var delete = Loc.Get("Body_DeleteWeighing");
        if (await _dialogs.ChooseAsync($"{Loc.Day(weight.Date)} · {Loc.Weight(weight.Kilograms)}", delete) == delete)
            await _store.DeleteWeightAsync(weight.Id);
    }

    private async Task EditMeasurementAsync(MeasurementItem item)
    {
        var title = Loc.Get($"Measure_{item.Kind}");
        var value = await _dialogs.PromptNumberAsync(title, Loc.Get("Body_MeasureText"), item.Value);
        if (value is not { } centimetres)
            return;
        try
        {
            await _store.SaveMeasurementAsync(DateTime.Today, item.Kind, centimetres);
            Palette.Haptic();
        }
        catch (FonteException ex)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
        }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (await _authentication.AuthenticateAsync(Loc.Get("Body_UnlockReason")))
        {
            _isUnlocked = true;
            RequestReload();
        }
    }

    [RelayCommand]
    private async Task AddPhotoAsync()
    {
        if (IsLocked)
        {
            await UnlockAsync();
            if (IsLocked && !_isUnlocked)
                return;
        }
        var camera = Loc.Get("Body_TakePhoto");
        var library = Loc.Get("Body_PickPhoto");
        var options = MediaPicker.Default.IsCaptureSupported ? new[] { camera, library } : [library];
        var choice = await _dialogs.ChooseAsync(Loc.Get("Body_AddPhoto"), null, options);
        if (choice is null)
            return;

        try
        {
            if (await _photos.AddAsync(choice == camera) is { } fileName)
            {
                await _store.AddPhotoAsync(DateTime.Now, fileName);
                SuccessToast.Show(Loc.Get("Body_PhotoSaved"));
            }
        }
        catch (PermissionException)
        {
            await _dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Get("Body_CameraDenied"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Photo not added: {ex}");
        }
    }

    [RelayCommand]
    private Task CompareAsync() => Shell.Current.GoToAsync(Routes.ComparePhotos);

    private Task OpenPhotoAsync(int photoId) => Shell.Current.GoToAsync($"{Routes.Photo}?id={photoId}");
}

public sealed class WeighingItem(BodyWeight weight, Func<BodyWeight, Task> delete)
{
    public string DayText { get; } = Loc.Day(weight.Date);

    public string ValueText { get; } = Loc.Weight(weight.Kilograms);

    public IAsyncRelayCommand MenuCommand { get; } = new AsyncRelayCommand(() => delete(weight));
}

/// <summary>The last value of a measurement and how it moved since the one before.</summary>
public sealed class MeasurementItem
{
    public MeasurementItem(MeasurementKind kind, BodyMeasurement? last, BodyMeasurement? before, Func<MeasurementItem, Task> edit)
    {
        Kind = kind;
        Value = last?.Centimetres;
        Name = Loc.Get($"Measure_{kind}");
        ValueText = last is null ? "—" : $"{Loc.Number(last.Centimetres)} cm";
        DeltaText = last is not null && before is not null ? BodyViewModel.Signed(last.Centimetres - before.Centimetres, "cm") : string.Empty;
        DateText = last is null ? Loc.Get("Body_TapToMeasure") : Loc.Day(last.Date);
        EditCommand = new AsyncRelayCommand(() => edit(this));
    }

    public MeasurementKind Kind { get; }

    public double? Value { get; }

    public string Name { get; }

    public string ValueText { get; }

    public string DeltaText { get; }

    public string DateText { get; }

    public IAsyncRelayCommand EditCommand { get; }
}

public sealed class PhotoItem(BodyPhoto photo, Func<int, Task> open)
{
    public ImageSource Image { get; } = ImageSource.FromFile(PhotoStore.PathOf(photo.FileName));

    public string DayText { get; } = photo.Date.ToString("d MMM yyyy", Loc.Culture);

    public IAsyncRelayCommand OpenCommand { get; } = new AsyncRelayCommand(() => open(photo.Id));
}
