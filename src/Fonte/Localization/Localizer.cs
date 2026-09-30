using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Fonte.Localization;

/// <param name="Code">Stored in preferences ("fr", "nl", "en").</param>
/// <param name="NativeName">Shown in the language picker, always in its own language.</param>
/// <param name="CultureName">Drives number, currency and date formats.</param>
public sealed record AppLanguage(string Code, string NativeName, string CultureName);

/// <summary>
/// Current language of the app. Texts bound through <see cref="TrExtension"/> refresh as soon as it changes,
/// without restarting the app.
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<AppLanguage> Languages =
    [
        new("fr", "Français", "fr-FR"),
        new("nl", "Nederlands", "nl-NL"),
        new("en", "English", "en-GB"),
    ];

    /// <summary>The phone's language when the app supports it, English otherwise. Read once, before any override.</summary>
    public static readonly AppLanguage DeviceLanguage =
        Find(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? Languages.Single(l => l.Code == "en");

    private static readonly ResourceManager Strings =
        new("Fonte.Resources.Strings.AppResources", typeof(Localizer).Assembly);

    private Localizer()
    {
        Language = DeviceLanguage;
        Culture = CultureInfo.GetCultureInfo(Language.CultureName);
    }

    public static Localizer Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language { get; private set; }

    public CultureInfo Culture { get; private set; }

    public string this[string key] => Strings.GetString(key, Culture) ?? key;

    public static AppLanguage? Find(string? code) => Languages.FirstOrDefault(l => l.Code == code);

    public void SetLanguage(AppLanguage language)
    {
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language.CultureName);

        // Dates shown by native controls (DatePicker) follow the chosen language. Only the process-wide default is
        // set: assigning CultureInfo.CurrentCulture flows with the async context and would be reverted as soon as
        // the async method that changed the language returns.
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;

        // An empty property name tells bindings that every value, including indexer ones, changed.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
