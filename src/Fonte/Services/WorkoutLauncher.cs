using Fonte.Core.Data;
using Fonte.Localization;

namespace Fonte.Services;

/// <summary>Starts a workout from anywhere in the app, or brings back the one already in progress.</summary>
public sealed class WorkoutLauncher(FonteStore store, IDialogService dialogs)
{
    public async Task StartFreeAsync()
    {
        // Resumes the workout in progress if there is one.
        await store.StartWorkoutAsync(DateTime.Now);
        Palette.Haptic();
        await Shell.Current.GoToAsync(Routes.Workout);
    }

    /// <summary>Starts a workout of a template, with its exercises and targets.</summary>
    public async Task StartTemplateAsync(int templateId)
    {
        if (await store.GetWorkoutInProgressAsync() is { } inProgress)
        {
            if (inProgress.TemplateId != templateId)
            {
                var resume = await dialogs.ConfirmAsync(
                    Loc.Get("Launch_InProgressTitle"), Loc.Get("Launch_InProgressText"), Loc.Get("Home_Resume"));
                if (!resume)
                    return;
            }
            await Shell.Current.GoToAsync(Routes.Workout);
            return;
        }

        try
        {
            await store.StartWorkoutFromTemplateAsync(templateId, DateTime.Now);
        }
        catch (Core.Models.FonteException ex)
        {
            await dialogs.AlertAsync(Loc.Get("Common_Oops"), Loc.Error(ex));
            return;
        }
        Palette.Haptic();
        await Shell.Current.GoToAsync(Routes.Workout);
    }
}
