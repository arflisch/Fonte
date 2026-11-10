using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fonte.Core.Data;
using Fonte.Core.Models;
using Fonte.Localization;
using Fonte.Services;

namespace Fonte.ViewModels;

/// <summary>
/// CrossFit WOD timer (Fonte Pro): a 10-second countdown, then AMRAP (time left, rounds counted with one tap),
/// EMOM (the minute in progress, a buzz at each new one) or for time (time going up to the cap, stopped when the
/// work is done). The score is kept with the workout.
/// </summary>
public sealed partial class WodViewModel(FonteStore store) : ObservableObject, IQueryAttributable, ISheetViewModel
{
    private const int CountdownSeconds = 10;
    private static readonly int[] MinuteChoices = [5, 8, 10, 12, 15, 20, 25, 30];

    private int _workoutId;
    private IDispatcherTimer? _timer;
    private DateTime _startedAt;
    private TimeSpan _pausedFor;
    private DateTime? _pausedAt;
    private int _lastMinute = -1;
    private bool _isSaving;

    public System.Windows.Input.ICommand DismissCommand => CloseCommand;

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _formats = [];

    [ObservableProperty]
    private IReadOnlyList<SelectableOption> _minuteOptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAmrap), nameof(IsForTime), nameof(IsEmom))]
    private WodFormat _format = WodFormat.Amrap;

    [ObservableProperty]
    private int _minutes = 12;

    public bool IsAmrap => Format == WodFormat.Amrap;

    public bool IsEmom => Format == WodFormat.Emom;

    public bool IsForTime => Format == WodFormat.ForTime;

    /// <summary>Choosing the format and duration, before the start.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunningOrDone))]
    private bool _isSetup = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunningOrDone))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunningOrDone))]
    private bool _isDone;

    public bool IsRunningOrDone => !IsSetup;

    [ObservableProperty]
    private bool _isCountdown;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _clockText = "0:00";

    [ObservableProperty]
    private string _clockLabel = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private int _rounds;

    [ObservableProperty]
    private string _repsText = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    /// <summary>The repetitions of a round not finished count: AMRAP, or "for time" stopped by the cap.</summary>
    [ObservableProperty]
    private bool _asksExtraReps;

    private int _seconds;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("workout", out var id) && int.TryParse(id?.ToString(), out var workoutId))
            _workoutId = workoutId;
        if (query.TryGetValue("format", out var f) && Enum.TryParse<WodFormat>(f?.ToString(), out var format) && format != WodFormat.None)
            Format = format;
        if (query.TryGetValue("minutes", out var m) && int.TryParse(m?.ToString(), out var minutes) && minutes > 0)
            Minutes = minutes;
        Formats = new[] { WodFormat.Amrap, WodFormat.Emom, WodFormat.ForTime }
            .Select((x, i) => new SelectableOption(x.ToString(), i, 3, o => { Format = Enum.Parse<WodFormat>(o.Value); ShowSetup(); })
            {
                Label = Loc.Get($"Wod_{x}"),
            })
            .ToList();
        MinuteOptions = MinuteChoices.Union([Minutes]).Order()
            .Select((x, i) => new SelectableOption(x.ToString(CultureInfo.InvariantCulture), i, int.MaxValue, o => { Minutes = int.Parse(o.Value, CultureInfo.InvariantCulture); ShowSetup(); })
            {
                Label = $"{x} min",
            })
            .ToList();
        ShowSetup();
    }

    private void ShowSetup()
    {
        foreach (var o in Formats)
            o.IsSelected = o.Value == Format.ToString();
        foreach (var o in MinuteOptions)
            o.IsSelected = o.Value == Minutes.ToString(CultureInfo.InvariantCulture);
        Title = Loc.Wod(Format, Minutes);
        ClockLabel = Loc.Get($"Wod_{Format}Hint");
    }

    [RelayCommand]
    private void Start()
    {
        IsSetup = false;
        IsRunning = true;
        IsCountdown = true;
        _startedAt = DateTime.Now.AddSeconds(CountdownSeconds);
        _pausedFor = TimeSpan.Zero;
        _lastMinute = -1;
        Rounds = 0;
        SetKeepScreenOn(true);
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(200);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        Tick();
    }

    private void OnTick(object? sender, EventArgs e) => Tick();

    private TimeSpan Elapsed => (_pausedAt ?? DateTime.Now) - _startedAt - _pausedFor;

    private void Tick()
    {
        if (!IsRunning)
            return;
        var elapsed = Elapsed;
        if (elapsed < TimeSpan.Zero)
        {
            IsCountdown = true;
            ClockText = Math.Ceiling(-elapsed.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            ClockLabel = Loc.Get("Wod_GetReady");
            Progress = 0;
            return;
        }
        if (IsCountdown)
        {
            IsCountdown = false;
            Palette.Vibrate();
        }

        var total = TimeSpan.FromMinutes(Minutes);
        switch (Format)
        {
            case WodFormat.Amrap:
                ClockText = Loc.Clock(total - elapsed);
                ClockLabel = Loc.Get("Wod_TimeLeft");
                break;
            case WodFormat.Emom:
                var minute = (int)elapsed.TotalMinutes;
                if (minute != _lastMinute && minute < Minutes)
                {
                    if (_lastMinute >= 0)
                        Palette.Vibrate();
                    _lastMinute = minute;
                    Rounds = minute;
                }
                ClockText = Loc.Clock(TimeSpan.FromMinutes(minute + 1) - elapsed);
                ClockLabel = Loc.Format("Wod_EmomMinute", Math.Min(minute + 1, Minutes), Minutes);
                break;
            default:
                ClockText = Loc.Clock(elapsed);
                ClockLabel = Minutes > 0 ? Loc.Format("Wod_Cap", Minutes) : string.Empty;
                break;
        }
        Progress = Math.Min(1, elapsed / total);
        if (elapsed >= total)
            Finish(timeCapped: true);
    }

    [RelayCommand]
    private void AddRound()
    {
        if (!IsRunning || IsCountdown)
            return;
        Rounds++;
        Palette.Haptic();
    }

    [RelayCommand]
    private void RemoveRound()
    {
        if (Rounds > 0)
            Rounds--;
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsRunning)
            return;
        if (_pausedAt is { } at)
        {
            _pausedFor += DateTime.Now - at;
            _pausedAt = null;
            IsPaused = false;
        }
        else
        {
            _pausedAt = DateTime.Now;
            IsPaused = true;
        }
    }

    /// <summary>For time: the work is done, the clock stops.</summary>
    [RelayCommand]
    private void Done() => Finish(timeCapped: false);

    private void Finish(bool timeCapped)
    {
        if (!IsRunning)
            return;
        var elapsed = Elapsed;
        IsRunning = false;
        IsDone = true;
        _timer?.Stop();
        SetKeepScreenOn(false);
        Palette.Vibrate();
        switch (Format)
        {
            case WodFormat.Emom:
                Rounds = Minutes;
                ResultText = Loc.Format("Wod_ScoreEmom", Minutes, Minutes);
                break;
            case WodFormat.ForTime:
                _seconds = timeCapped ? 0 : (int)Math.Round(Math.Max(0, elapsed.TotalSeconds));
                ResultText = timeCapped ? Loc.Get("Wod_ScoreCap") : Loc.Clock(TimeSpan.FromSeconds(_seconds));
                AsksExtraReps = timeCapped;
                break;
            default:
                ResultText = Loc.Format("Wod_ScoreRounds", Rounds);
                AsksExtraReps = true;
                break;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_isSaving || _workoutId == 0)
            return;
        _isSaving = true;
        try
        {
            var reps = int.TryParse(RepsText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? Math.Clamp(r, 0, 999) : 0;
            await store.SaveWodScoreAsync(_workoutId, Format, Minutes, Rounds, reps, _seconds);
            Palette.Haptic();
            SuccessToast.Show(Loc.Get("Wod_Saved"));
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private Task CloseAsync()
    {
        _timer?.Stop();
        SetKeepScreenOn(false);
        return Shell.Current.GoToAsync("..");
    }

    private static void SetKeepScreenOn(bool on)
    {
        try
        {
            DeviceDisplay.Current.KeepScreenOn = on;
        }
        catch (Exception)
        {
            // Not every platform can keep the screen on.
        }
    }
}
