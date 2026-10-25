using Fonte.Localization;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;

namespace Fonte.Services;

/// <summary>Daily notifications of Fonte Pro nutrition: logging the meals, drinking, the fasting window.</summary>
public sealed class NutritionReminders(INotificationService notifications, AppSettings settings, ProService pro)
{
    private static readonly (int Id, TimeSpan At)[] Meals =
    [
        (7101, new TimeSpan(9, 30, 0)),
        (7102, new TimeSpan(13, 30, 0)),
        (7103, new TimeSpan(20, 30, 0)),
    ];

    private static readonly int[] WaterIds = [7111, 7112, 7113, 7114, 7115, 7116];

    private const int FastingOpensId = 7121, FastingClosesId = 7122;

    /// <summary>Schedules again what the settings ask for, after cancelling every nutrition reminder.</summary>
    public async Task UpdateAsync()
    {
        try
        {
            if (!notifications.IsSupported)
                return;
            notifications.Cancel([.. Meals.Select(m => m.Id), .. WaterIds, FastingOpensId, FastingClosesId]);
            if (!pro.IsUnlocked)
                return;

            var requests = new List<NotificationRequest>();
            if (settings.MealReminders)
            {
                string[] texts = [Loc.Get("Reminder_Breakfast"), Loc.Get("Reminder_Lunch"), Loc.Get("Reminder_Dinner")];
                for (var i = 0; i < Meals.Length; i++)
                    requests.Add(Daily(Meals[i].Id, Meals[i].At, Loc.Get("Reminder_MealTitle"), texts[i]));
            }
            if (settings.WaterReminders)
            {
                // Every two hours from 10:00 to 20:00.
                for (var i = 0; i < WaterIds.Length; i++)
                    requests.Add(Daily(WaterIds[i], TimeSpan.FromHours(10 + 2 * i), Loc.Get("Reminder_WaterTitle"), Loc.Get("Reminder_WaterText")));
            }
            if (settings.FastingReminders && settings.FastingHours > 0)
            {
                var opens = TimeSpan.FromHours(settings.EatingStartHour);
                var closes = TimeSpan.FromHours((settings.EatingStartHour + 24 - settings.FastingHours) % 24);
                requests.Add(Daily(FastingOpensId, opens, Loc.Get("Reminder_FastingTitle"), Loc.Get("Reminder_FastingOpens")));
                requests.Add(Daily(FastingClosesId, closes - TimeSpan.FromHours(1), Loc.Get("Reminder_FastingTitle"), Loc.Get("Reminder_FastingCloses")));
            }
            if (requests.Count == 0)
                return;

            if (!await notifications.AreNotificationsEnabled())
                await notifications.RequestNotificationPermission();
            foreach (var request in requests)
                await notifications.Show(request);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Nutrition reminders not scheduled: {ex}");
        }
    }

    private static NotificationRequest Daily(int id, TimeSpan at, string title, string text)
    {
        at = TimeSpan.FromMinutes(((int)at.TotalMinutes % 1440 + 1440) % 1440);
        var first = DateTime.Today + at;
        if (first <= DateTime.Now)
            first = first.AddDays(1);
        return new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = text,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = first,
                RepeatType = NotificationRepeat.Daily,
                Android = new AndroidScheduleOptions { ScheduleMode = AndroidScheduleMode.Default },
            },
        };
    }
}
