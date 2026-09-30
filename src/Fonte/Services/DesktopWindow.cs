namespace Fonte.Services;

/// <summary>
/// On a computer, opens the window at a comfortable, phone-like size and remembers the size the user picks.
/// </summary>
public static class DesktopWindow
{
    private const string WidthKey = "window_width";
    private const string HeightKey = "window_height";

    public static void Configure(Window window, IPreferences preferences)
    {
        if (DeviceInfo.Idiom != DeviceIdiom.Desktop)
            return;

        // In points of the Mac screen.
        window.MinimumWidth = 420;
        window.MinimumHeight = 600;
        window.Width = preferences.Get(WidthKey, 560d);
        window.Height = preferences.Get(HeightKey, 820d);

        window.SizeChanged += (_, _) =>
        {
            if (window.Width < window.MinimumWidth || window.Height < window.MinimumHeight)
                return;
            preferences.Set(WidthKey, window.Width);
            preferences.Set(HeightKey, window.Height);
        };
    }
}
