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
    private const string LastBackupKey = "last_backup";
    private const string HealthKey = "health";
    private const string NutritionKey = "nutrition_profile";
    private const string WaterGoalKey = "water_goal";
    private const string ActiveEnergyKey = "count_active_energy";
    private const string ProHintKey = "pro_hint_shown";
    private const string PoundsKey = "pounds";
    private const string BarPoundsKey = "bar_weight_lb";
    private const string PlatesPoundsKey = "plates_lb";
    private const string FastingKey = "fasting_hours";
    private const string EatingStartKey = "eating_start";
    private const string MealRemindersKey = "meal_reminders";
    private const string WaterRemindersKey = "water_reminders";
    private const string FastingRemindersKey = "fasting_reminders";
    private const string DayKindsKey = "day_kinds";

    /// <summary>Rest durations offered, in seconds.</summary>
    public static readonly IReadOnlyList<int> RestChoices = [30, 45, 60, 90, 120, 150, 180, 240, 300];

    /// <summary>Workouts per week that can be aimed for.</summary>
    public static readonly IReadOnlyList<int> SessionChoices = [2, 3, 4, 5, 6];

    private readonly IPreferences _preferences;

    public AppSettings(IPreferences preferences)
    {
        _preferences = preferences;
        Localizer.Instance.SetLanguage(Language);
        Loc.Pounds = UsesPounds;
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

    /// <summary>
    /// Loads and body weight in pounds rather than kilograms. Defaults to pounds where the phone's region doesn't
    /// use the metric system (United States…).
    /// </summary>
    public bool UsesPounds
    {
        get => _preferences.Get(PoundsKey, !RegionInfo.CurrentRegion.IsMetric);
        set => Update(() =>
        {
            _preferences.Set(PoundsKey, value);
            Loc.Pounds = value;
        });
    }

    /// <summary>Weight of the bar used by the plate calculator, in the unit shown.</summary>
    public double BarWeight
    {
        get => UsesPounds ? _preferences.Get(BarPoundsKey, 45.0) : _preferences.Get(BarKey, 20.0);
        set => Update(() => _preferences.Set(UsesPounds ? BarPoundsKey : BarKey, value));
    }

    /// <summary>Plates the gym has, heaviest first, in the unit shown (a gym in pounds has its own plates).</summary>
    public IReadOnlyList<double> Plates
    {
        get
        {
            var stored = _preferences.Get<string?>(UsesPounds ? PlatesPoundsKey : PlatesKey, null);
            if (string.IsNullOrEmpty(stored))
                return UsesPounds ? Core.Training.Plates.StandardPounds : Core.Training.Plates.Standard;
            return stored.Split(';')
                .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
                .Where(v => v > 0)
                .OrderDescending()
                .ToList();
        }
        set => Update(() => _preferences.Set(UsesPounds ? PlatesPoundsKey : PlatesKey,
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

    /// <summary>When a backup was last saved or sent; null before the first one.</summary>
    public DateTimeOffset? LastBackupAt
    {
        get => _preferences.Get(LastBackupKey, 0L) is > 0 and var seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        set => Update(() => _preferences.Set(LastBackupKey, value?.ToUnixTimeSeconds() ?? 0L));
    }

    /// <summary>Workouts saved to Apple Health and body weight read from it (Fonte Pro).</summary>
    public bool HealthEnabled
    {
        get => _preferences.Get(HealthKey, false);
        set => Update(() => _preferences.Set(HealthKey, value));
    }

    /// <summary>What the calorie target is computed from; null until the user set it up.</summary>
    public Core.Nutrition.NutritionProfile? NutritionProfile
    {
        get
        {
            var stored = _preferences.Get<string?>(NutritionKey, null);
            if (string.IsNullOrEmpty(stored))
                return null;
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<Core.Nutrition.NutritionProfile>(stored);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
        set => Update(() => _preferences.Set(NutritionKey, value is null ? null : System.Text.Json.JsonSerializer.Serialize(value)));
    }

    /// <summary>Water to drink each day, in millilitres.</summary>
    public int WaterGoalMl
    {
        get => _preferences.Get(WaterGoalKey, 2000);
        set => Update(() => _preferences.Set(WaterGoalKey, Math.Clamp(value, 500, 6000)));
    }

    /// <summary>The calories burnt in activities (from Apple Health) are added to the day's budget.</summary>
    public bool CountActiveEnergy
    {
        get => _preferences.Get(ActiveEnergyKey, false);
        set => Update(() => _preferences.Set(ActiveEnergyKey, value));
    }

    /// <summary>Hours of fasting each day (16 for 16:8); 0 when the user doesn't fast.</summary>
    public int FastingHours
    {
        get => _preferences.Get(FastingKey, 0);
        set => Update(() => _preferences.Set(FastingKey, Math.Clamp(value, 0, 23)));
    }

    /// <summary>Hour the eating window opens (12 for noon).</summary>
    public int EatingStartHour
    {
        get => _preferences.Get(EatingStartKey, 12);
        set => Update(() => _preferences.Set(EatingStartKey, Math.Clamp(value, 0, 23)));
    }

    /// <summary>A reminder to log breakfast, lunch and dinner.</summary>
    public bool MealReminders
    {
        get => _preferences.Get(MealRemindersKey, false);
        set => Update(() => _preferences.Set(MealRemindersKey, value));
    }

    /// <summary>A reminder to drink every two hours during the day.</summary>
    public bool WaterReminders
    {
        get => _preferences.Get(WaterRemindersKey, false);
        set => Update(() => _preferences.Set(WaterRemindersKey, value));
    }

    /// <summary>A notification when the eating window opens and an hour before it closes.</summary>
    public bool FastingReminders
    {
        get => _preferences.Get(FastingRemindersKey, false);
        set => Update(() => _preferences.Set(FastingRemindersKey, value));
    }

    /// <summary>
    /// Days the user said were (true) or weren't (false) training days, over what the workouts say; the last
    /// 60 days only.
    /// </summary>
    public IReadOnlyDictionary<DateTime, bool> DayKinds
    {
        get
        {
            var days = new Dictionary<DateTime, bool>();
            foreach (var part in (_preferences.Get<string?>(DayKindsKey, null) ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=');
                if (pieces.Length == 2 && DateTime.TryParseExact(pieces[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
                    days[day] = pieces[1] == "1";
            }
            return days;
        }
    }

    public void SetDayKind(DateTime day, bool? training)
    {
        var days = DayKinds.Where(d => d.Key >= DateTime.Today.AddDays(-60) && d.Key != day.Date).ToDictionary();
        if (training is { } value)
            days[day.Date] = value;
        Update(() => _preferences.Set(DayKindsKey,
            string.Join(';', days.Select(d => $"{d.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}={(d.Value ? 1 : 0)}"))));
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
