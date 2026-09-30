using Fonte.Core.Data;
using Fonte.Localization;
using Fonte.Services;
using Fonte.ViewModels;
using Fonte.Views;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

namespace Fonte;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseLocalNotification()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        ConfigureInputs();

        builder.Services.AddSingleton(_ => new FonteStore(Path.Combine(FileSystem.AppDataDirectory, "fonte.db3")));
        builder.Services.AddSingleton(Preferences.Default);
        builder.Services.AddSingleton<AppSettings>();
        builder.Services.AddSingleton<RestTimer>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<AppShell>();

        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<ExercisesViewModel>();
        builder.Services.AddSingleton<ExercisesPage>();
        builder.Services.AddTransient<WorkoutViewModel>();
        builder.Services.AddTransient<WorkoutPage>();
        builder.Services.AddTransient<ExercisePickerViewModel>();
        builder.Services.AddTransient<ExercisePickerPage>();
        builder.Services.AddTransient<ExerciseDetailViewModel>();
        builder.Services.AddTransient<ExerciseDetailPage>();
        builder.Services.AddTransient<CustomExerciseViewModel>();
        builder.Services.AddTransient<CustomExercisePage>();
        builder.Services.AddTransient<SummaryViewModel>();
        builder.Services.AddTransient<SummaryPage>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void ConfigureInputs()
    {
        // Inputs are drawn inside our own rounded fields, so remove the native underline/border.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("Borderless", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#elif IOS || MACCATALYST
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        });

        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("Borderless", (handler, _) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
        });

        // Loads and repetitions are pre-filled: focusing a field selects its value, so typing replaces it.
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NumericFields", (handler, view) =>
        {
            if (view.Keyboard != Keyboard.Numeric)
                return;
#if ANDROID
            handler.PlatformView.SetSelectAllOnFocus(true);
#elif IOS || MACCATALYST
            // A tap places the caret once the field is focused: select a moment later, and through the text range
            // rather than selectAll:, which would pop up the Copy/Paste menu.
            var field = handler.PlatformView;
            field.EditingDidBegin += (_, _) => CoreFoundation.DispatchQueue.MainQueue.DispatchAfter(
                new CoreFoundation.DispatchTime(CoreFoundation.DispatchTime.Now, TimeSpan.FromMilliseconds(120)),
                () =>
                {
                    if (field.IsFirstResponder)
                        field.SelectedTextRange = field.GetTextRange(field.BeginningOfDocument, field.EndOfDocument);
                });
#endif
#if IOS && !MACCATALYST
            // The number pad has no return key: add a "Done" bar above it.
            var toolbar = new UIKit.UIToolbar(new CoreGraphics.CGRect(0, 0, 320, 44));
            toolbar.Items =
            [
                new UIKit.UIBarButtonItem(UIKit.UIBarButtonSystemItem.FlexibleSpace),
                new UIKit.UIBarButtonItem(Loc.Get("Common_Done"), UIKit.UIBarButtonItemStyle.Done, (_, _) => field.ResignFirstResponder()),
            ];
            toolbar.SizeToFit();
            field.InputAccessoryView = toolbar;
#endif
        });
    }
}
