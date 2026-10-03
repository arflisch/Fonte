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

#if ANDROID
        // Android draws an opaque bottom bar: match the app's surfaces and accent (iOS keeps its native glass bar).
        this.SetAppThemeColor(TabBarBackgroundColorProperty, Color.FromArgb("#FFFFFF"), Color.FromArgb("#161922"));
        this.SetAppThemeColor(TabBarForegroundColorProperty, Color.FromArgb("#534AB7"), Color.FromArgb("#AFA9EC"));
        this.SetAppThemeColor(TabBarTitleColorProperty, Color.FromArgb("#534AB7"), Color.FromArgb("#AFA9EC"));
        this.SetAppThemeColor(TabBarUnselectedColorProperty, Color.FromArgb("#94A3B8"), Color.FromArgb("#5B6475"));
#endif
    }
}
