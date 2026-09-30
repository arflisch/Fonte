using Fonte.Localization;

namespace Fonte.Services;

/// <summary>User preferences persisted with <see cref="IPreferences"/>.</summary>
public sealed class AppSettings
{
    private const string LanguageKey = "language";
    private const string RestSecondsKey = "rest_seconds";
    private const string RestTimerKey = "rest_timer";

    /// <summary>Rest durations offered, in seconds.</summary>
    public static readonly IReadOnlyList<int> RestChoices = [30, 45, 60, 90, 120, 150, 180, 240, 300];

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

    private void Update(Action write)
    {
        write();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
