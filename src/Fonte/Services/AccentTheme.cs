#if IOS || MACCATALYST
using Microsoft.Maui.Platform;
using UIKit;
#endif
using Fonte.Localization;

namespace Fonte.Services;

/// <param name="Key">Stored in preferences.</param>
/// <param name="Base">The colour itself: buttons, links, selection in the light theme.</param>
/// <param name="Deep">Darker shade: start of the gradients, buttons on dark surfaces.</param>
/// <param name="Light">Lighter shade: end of the gradients.</param>
/// <param name="OnDark">Text and icons in the accent colour on the dark theme.</param>
/// <param name="SoftLight">Very light tint behind selected items, light theme.</param>
/// <param name="SoftDark">Deep tint behind selected items, dark theme.</param>
/// <param name="Track">Empty part of progress bars on a soft background, light theme.</param>
public sealed record AccentColor(
    string Key, Color Base, Color Deep, Color Light, Color OnDark, Color SoftLight, Color SoftDark, Color Track)
{
    public string Name => Key switch
    {
        "blue" => Loc.Get("Accent_Blue"),
        "teal" => Loc.Get("Accent_Teal"),
        "green" => Loc.Get("Accent_Green"),
        "orange" => Loc.Get("Accent_Orange"),
        "red" => Loc.Get("Accent_Red"),
        "pink" => Loc.Get("Accent_Pink"),
        "slate" => Loc.Get("Accent_Slate"),
        _ => Loc.Get("Accent_Violet"),
    };
}

/// <summary>
/// The app's accent colour, chosen in the settings. Pages refer to it through dynamic resources ("Accent",
/// "AccentSoft", "HeroGradient"…), so a new choice, or a switch between light and dark, repaints them at once.
/// </summary>
public static class AccentTheme
{
    public static readonly IReadOnlyList<AccentColor> All =
    [
        new("violet", C("#534AB7"), C("#3C3489"), C("#7F77DD"), C("#AFA9EC"), C("#EEEDFE"), C("#26215C"), C("#D6D3F7")),
        new("blue", C("#2563EB"), C("#1E40AF"), C("#60A5FA"), C("#93C5FD"), C("#EFF6FF"), C("#172554"), C("#BFDBFE")),
        new("teal", C("#0D9488"), C("#115E59"), C("#2DD4BF"), C("#5EEAD4"), C("#F0FDFA"), C("#042F2E"), C("#99F6E4")),
        new("green", C("#16A34A"), C("#166534"), C("#4ADE80"), C("#86EFAC"), C("#F0FDF4"), C("#052E16"), C("#BBF7D0")),
        new("orange", C("#EA580C"), C("#C2410C"), C("#FB923C"), C("#FDBA74"), C("#FFF7ED"), C("#431407"), C("#FED7AA")),
        new("red", C("#DC2626"), C("#991B1B"), C("#F87171"), C("#FCA5A5"), C("#FEF2F2"), C("#450A0A"), C("#FECACA")),
        new("pink", C("#DB2777"), C("#9D174D"), C("#F472B6"), C("#F9A8D4"), C("#FDF2F8"), C("#500724"), C("#FBCFE8")),
        new("slate", C("#475569"), C("#1E293B"), C("#94A3B8"), C("#CBD5E1"), C("#F1F5F9"), C("#1E293B"), C("#CBD5E1")),
    ];

    private static bool _followsTheme;

    /// <summary>Raised after the colour (or the light/dark theme) changed, for what resources can't reach.</summary>
    public static event EventHandler? Changed;

    public static AccentColor Current { get; private set; } = All[0];

    public static AccentColor Find(string? key) => All.FirstOrDefault(a => a.Key == key) ?? All[0];

    public static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <summary>The accent colour in the current theme: <see cref="AccentColor.Base"/>, or its light shade on dark.</summary>
    public static Color Text => IsDark ? Current.OnDark : Current.Base;

    public static void Apply(AccentColor accent)
    {
        Current = accent;
        if (Application.Current is not { } app)
            return;
        if (!_followsTheme)
        {
            _followsTheme = true;
            app.RequestedThemeChanged += (_, _) => Apply(Current);
        }

        var dark = IsDark;
        var resources = app.Resources;
        resources["Primary"] = accent.Base;
        resources["PrimaryDarkMode"] = accent.OnDark;
        resources["PrimarySoftLight"] = accent.SoftLight;
        resources["PrimarySoftDark"] = accent.SoftDark;
        resources["Accent"] = dark ? accent.OnDark : accent.Base;
        resources["AccentSoft"] = dark ? accent.SoftDark : accent.SoftLight;
        resources["AccentTint"] = accent.Base.WithAlpha(0.15f);
        resources["AccentInk"] = dark ? Colors.White : accent.SoftDark;
        resources["AccentTrack"] = dark ? accent.Deep : accent.Track;
        resources["AccentButton"] = dark ? accent.Deep : Colors.White;
        resources["AccentOnButton"] = dark ? Colors.White : accent.Base;
        resources["HeroGradient"] = Gradient(accent, new Point(1, 1));
        resources["AccentGradient"] = Gradient(accent, new Point(1, 0));
        resources["FloatingShadow"] = new Shadow
        {
            Brush = new SolidColorBrush(accent.Base),
            Opacity = 0.35f,
            Radius = 20,
            Offset = new Point(0, 8),
        };
#if IOS || MACCATALYST
        RefreshTabBars();
#endif
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static LinearGradientBrush Gradient(AccentColor accent, Point end) => new(
        [new GradientStop(accent.Deep, 0f), new GradientStop(accent.Base, 0.55f), new GradientStop(accent.Light, 1f)],
        new Point(0, 0), end);

    private static Color C(string hex) => Color.FromArgb(hex);

#if IOS || MACCATALYST
    /// <summary>The selected tab, in the accent colour; resolved again when the phone switches light/dark.</summary>
    public static UIColor TabTint() =>
        new(traits => traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark
            ? Current.OnDark.ToPlatform()
            : Current.Base.ToPlatform());

    /// <summary>Tab bars already on screen keep the tint they were created with: give them the new one.</summary>
    private static void RefreshTabBars()
    {
        UITabBar.Appearance.TintColor = TabTint();
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>())
        foreach (var window in scene.Windows)
            Retint(window.RootViewController);
    }

    private static void Retint(UIViewController? controller)
    {
        if (controller is null)
            return;
        if (controller is UITabBarController tabs)
            tabs.TabBar.TintColor = TabTint();
        foreach (var child in controller.ChildViewControllers)
            Retint(child);
        Retint(controller.PresentedViewController);
    }
#endif
}
