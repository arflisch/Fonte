using Fonte.Localization;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;
using Plugin.LocalNotification.Core.Models.AppleOption;

namespace Fonte.Services;

/// <summary>
/// Countdown between two sets. Its end is saved, so it keeps counting if the app is closed, and a notification
/// rings at the end while the phone is locked.
/// </summary>
public sealed class RestTimer
{
    private const int NotificationId = 7001;
    private const string EndsAtKey = "rest_ends_at";
    private const string TotalKey = "rest_total_seconds";

    private readonly IPreferences _preferences;
    private readonly INotificationService _notifications;

    public RestTimer(IPreferences preferences, INotificationService notifications)
    {
        _preferences = preferences;
        _notifications = notifications;
        if (preferences.ContainsKey(EndsAtKey))
        {
            EndsAt = preferences.Get(EndsAtKey, DateTime.MinValue);
            TotalSeconds = preferences.Get(TotalKey, 0);
        }
    }

    /// <summary>When the rest ends; null when no rest is counting down.</summary>
    public DateTime? EndsAt { get; private set; }

    /// <summary>Length of the current rest, adjustments included.</summary>
    public int TotalSeconds { get; private set; }

    public TimeSpan Remaining => EndsAt is { } end && end > DateTime.Now ? end - DateTime.Now : TimeSpan.Zero;

    /// <summary>Share of the rest still to go, from 1 down to 0.</summary>
    public double Progress => TotalSeconds <= 0 ? 0 : Math.Clamp(Remaining.TotalSeconds / TotalSeconds, 0, 1);

    public void Start(int seconds)
    {
        TotalSeconds = seconds;
        SetEnd(DateTime.Now.AddSeconds(seconds));
    }

    /// <summary>Adds (or removes, when negative) time to the rest counting down.</summary>
    public void Adjust(int seconds)
    {
        if (EndsAt is not { } end || end <= DateTime.Now)
            return;

        var newEnd = end.AddSeconds(seconds);
        if (newEnd <= DateTime.Now.AddSeconds(1))
        {
            Stop();
            return;
        }
        TotalSeconds = Math.Max(1, TotalSeconds + seconds);
        SetEnd(newEnd);
    }

    public void Stop()
    {
        EndsAt = null;
        TotalSeconds = 0;
        _preferences.Remove(EndsAtKey);
        _preferences.Remove(TotalKey);
        try
        {
            if (_notifications.IsSupported)
                _notifications.Cancel(NotificationId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rest notification not cancelled: {ex}");
        }
    }

    /// <summary>Asks, once, for the permission to ring at the end of the rest while the phone is locked.</summary>
    public async Task EnsurePermissionAsync()
    {
        try
        {
            if (_notifications.IsSupported && !await _notifications.AreNotificationsEnabled())
                await _notifications.RequestNotificationPermission();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification permission not requested: {ex}");
        }
    }

    private void SetEnd(DateTime end)
    {
        EndsAt = end;
        _preferences.Set(EndsAtKey, end);
        _preferences.Set(TotalKey, TotalSeconds);
        _ = ScheduleAsync(end);
    }

    private async Task ScheduleAsync(DateTime end)
    {
        try
        {
            if (!_notifications.IsSupported)
                return;

            // Same id: a new rest replaces the one scheduled before.
            await _notifications.Show(new NotificationRequest
            {
                NotificationId = NotificationId,
                Title = Loc.Get("Rest_NotificationTitle"),
                Description = Loc.Get("Rest_NotificationText"),
                Schedule = new NotificationRequestSchedule
                {
                    NotifyTime = end,
                    // Exact when Android allows it ("Alarms & reminders"), a little late otherwise.
                    Android = new AndroidScheduleOptions { ScheduleMode = AndroidScheduleMode.Default },
                },
                // With the app open, the countdown itself shows the end of the rest.
                Apple = new AppleOptions { HideForegroundAlert = true, PlayForegroundSound = false },
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Rest notification not scheduled: {ex}");
        }
    }
}
