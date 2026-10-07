using System.Globalization;
using Fonte.Core.Catalog;
using Fonte.Core.Training;
using Fonte.Localization;

namespace Fonte.Services;

/// <summary>User preferences persisted with <see cref="IPreferences"/>.</summary>
public sealed class AppSettings
{
    private const string LanguageKey = "language";
    private const string RestSecondsKey = "rest_seconds";
    private const string RestTimerKey = "rest_timer";
    private const string OnboardedKey = "onboarded";
    private const string GoalKey = "goal";
    private const string SessionsKey = "sessions_per_week";
    private const string GoalWeightKey = "goal_weight";
    private const string BarKey = "bar_weight";
    private const string PlatesKey = "plates";
    private const string AccentKey = "accent";
    private const string HealthKey = "health";
    private const string ProHintKey = "pro_hint_shown";

    /// <summary>Rest durations offered, in seconds.</summary>
    public static readonly IReadOnlyList<int> RestChoices = [30, 45, 60, 90, 120, 150, 180, 240, 300];

    /// <summary>Workouts per week that can be aimed for.</summary>
    public static readonly IReadOnlyList<int> SessionChoices = [2, 3, 4, 5, 6];

    private readonly IPreferences _preferences;

    public AppSettings(IPreferences preferences)
    {
        _preferences = preferences;
        Localizer.Instance.SetLanguage(Language);
    }

    /// <summary>Raised when any setting changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The chosen language; defaults to the phone's language when supported.</summary>
    public AppLanguage Language
    {
        get => Localizer.Find(_preferences.Get<string?>(LanguageKey, null)) ?? Localizer.DeviceLanguage;
        set => Update(() =>
        {
            _preferences.Set(LanguageKey, value.Code);
            Localizer.Instance.SetLanguage(value);
        });
    }

    /// <summary>Whether ticking a set starts the rest countdown.</summary>
    public bool RestTimerEnabled
    {
        get => _preferences.Get(RestTimerKey, true);
        set => Update(() => _preferences.Set(RestTimerKey, value));
    }

    /// <summary>Length of the rest between two sets.</summary>
    public int RestSeconds
    {
        get => _preferences.Get(RestSecondsKey, 90);
        set => Update(() => _preferences.Set(RestSecondsKey, value));
    }

    /// <summary>The welcome screens were gone through (or skipped).</summary>
    public bool HasOnboarded
    {
        get => _preferences.Get(OnboardedKey, false);
        set => Update(() => _preferences.Set(OnboardedKey, value));
    }

    public TrainingGoal Goal
    {
        get => Enum.TryParse<TrainingGoal>(_preferences.Get<string?>(GoalKey, null), out var goal) ? goal : TrainingGoal.Muscle;
        set => Update(() => _preferences.Set(GoalKey, value.ToString()));
    }

    /// <summary>Workouts aimed for each week: the home screen counts the week against it.</summary>
    public int SessionsPerWeek
    {
        get => Math.Clamp(_preferences.Get(SessionsKey, 3), 1, 7);
        set => Update(() => _preferences.Set(SessionsKey, Math.Clamp(value, 1, 7)));
    }

    /// <summary>Body weight aimed for, in kilograms; 0 when none.</summary>
    public double GoalWeight
    {
        get => _preferences.Get(GoalWeightKey, 0.0);
        set => Update(() => _preferences.Set(GoalWeightKey, Math.Max(0, Math.Round(value, 1))));
    }

    /// <summary>Weight of the bar used by the plate calculator.</summary>
    public double BarWeight
    {
        get => _preferences.Get(BarKey, 20.0);
        set => Update(() => _preferences.Set(BarKey, value));
    }

    /// <summary>Plates the gym has, heaviest first.</summary>
    public IReadOnlyList<double> Plates
    {
        get
        {
            var stored = _preferences.Get<string?>(PlatesKey, null);
            if (string.IsNullOrEmpty(stored))
                return Core.Training.Plates.Standard;
            return stored.Split(';')
                .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .Where(v => v > 0)
                .OrderDescending()
                .ToList();
        }
        set => Update(() => _preferences.Set(PlatesKey,
            string.Join(';', value.Select(v => v.ToString(CultureInfo.InvariantCulture)))));
    }

    /// <summary>The app's colour (<see cref="AccentTheme"/>); violet by default.</summary>
    public AccentColor Accent
    {
        get => AccentTheme.Find(_preferences.Get<string?>(AccentKey, null));
        set => Update(() =>
        {
            _preferences.Set(AccentKey, value.Key);
            AccentTheme.Apply(value);
        });
    }

    /// <summary>Workouts saved to Apple Health and body weight read from it (Fonte Pro).</summary>
    public bool HealthEnabled
    {
        get => _preferences.Get(HealthKey, false);
        set => Update(() => _preferences.Set(HealthKey, value));
    }

    /// <summary>The one-time suggestion of Fonte Pro after a few workouts was shown.</summary>
    public bool ProHintShown
    {
        get => _preferences.Get(ProHintKey, false);
        set => _preferences.Set(ProHintKey, value);
    }

    private void Update(Action write)
    {
        write();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
