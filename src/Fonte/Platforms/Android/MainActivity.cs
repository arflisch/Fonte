using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;

namespace Fonte;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        UpdateSystemBarIcons();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        UpdateSystemBarIcons();
    }

    /// <summary>Dark status bar icons on the light theme, light icons on the dark theme.</summary>
    private void UpdateSystemBarIcons()
    {
        if (Window is null)
            return;

        var isDark = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
        if (controller is null)
            return;
        controller.AppearanceLightStatusBars = !isDark;
        controller.AppearanceLightNavigationBars = !isDark;
    }
}
