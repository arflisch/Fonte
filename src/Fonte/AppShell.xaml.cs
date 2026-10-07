using Fonte.Services;
using Fonte.Views;

namespace Fonte;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(Routes.Workout, typeof(WorkoutPage));
        Routing.RegisterRoute(Routes.PickExercises, typeof(ExercisePickerPage));
        Routing.RegisterRoute(Routes.Exercise, typeof(ExerciseDetailPage));
        Routing.RegisterRoute(Routes.EditExercise, typeof(CustomExercisePage));
        Routing.RegisterRoute(Routes.Summary, typeof(SummaryPage));
        Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
        Routing.RegisterRoute(Routes.Welcome, typeof(WelcomePage));
        Routing.RegisterRoute(Routes.Template, typeof(TemplatePage));
        Routing.RegisterRoute(Routes.History, typeof(HistoryPage));
        Routing.RegisterRoute(Routes.Plates, typeof(PlatesPage));
        Routing.RegisterRoute(Routes.Photo, typeof(PhotoPage));
        Routing.RegisterRoute(Routes.ComparePhotos, typeof(ComparePhotosPage));
        Routing.RegisterRoute(Routes.Pro, typeof(ProPage));

#if ANDROID
        // Android draws an opaque bottom bar: match the app's surfaces and accent (iOS keeps its native glass bar).
        this.SetAppThemeColor(TabBarBackgroundColorProperty, Color.FromArgb("#FFFFFF"), Color.FromArgb("#161922"));
        this.SetAppThemeColor(TabBarUnselectedColorProperty, Color.FromArgb("#94A3B8"), Color.FromArgb("#5B6475"));
        ColorTabs();
        AccentTheme.Changed += (_, _) => ColorTabs();
#endif
    }

#if ANDROID
    private void ColorTabs()
    {
        var accent = AccentTheme.Current;
        this.SetAppThemeColor(TabBarForegroundColorProperty, accent.Base, accent.OnDark);
        this.SetAppThemeColor(TabBarTitleColorProperty, accent.Base, accent.OnDark);
    }
#endif
}
