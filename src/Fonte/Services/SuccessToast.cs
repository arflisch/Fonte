#if IOS || MACCATALYST
using CoreGraphics;
using Microsoft.Maui.Platform;
using UIKit;
#endif

namespace Fonte.Services;

/// <summary>
/// Brief confirmation that something was saved: a green check and a short message in the middle of the screen,
/// above any sheet, that fades away on its own and never blocks a tap.
/// </summary>
public static class SuccessToast
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1.2);

    public static void Show(string message) => MainThread.BeginInvokeOnMainThread(() =>
    {
#if IOS || MACCATALYST
        ShowApple(message);
#elif ANDROID
        Android.Widget.Toast.MakeText(Platform.AppContext, $"✓ {message}", Android.Widget.ToastLength.Short)?.Show();
        Palette.Haptic();
#endif
    });

#if IOS || MACCATALYST
    private static void ShowApple(string message)
    {
        var windows = UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .ToList();
        if ((windows.FirstOrDefault(w => w.IsKeyWindow) ?? windows.FirstOrDefault()) is not { } window)
            return;

        var blur = new UIVisualEffectView(UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemMaterial))
        {
            TranslatesAutoresizingMaskIntoConstraints = false,
            UserInteractionEnabled = false,
            Alpha = 0,
            Transform = CGAffineTransform.MakeScale(0.85f, 0.85f),
        };
        blur.Layer.CornerRadius = 24;
        blur.Layer.MasksToBounds = true;

        var check = new UIImageView(UIImage.GetSystemImage(
            "checkmark.circle.fill", UIImageSymbolConfiguration.Create(52, UIImageSymbolWeight.Semibold)))
        {
            TintColor = Color.FromArgb("#10B981").ToPlatform(),
            ContentMode = UIViewContentMode.Center,
        };
        var label = new UILabel
        {
            Text = message,
            Font = UIFont.FromName("OpenSans-Semibold", 16) ?? UIFont.SystemFontOfSize(16, UIFontWeight.Semibold)!,
            TextColor = UIColor.Label,
            TextAlignment = UITextAlignment.Center,
            Lines = 2,
        };
        var stack = new UIStackView([check, label])
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Alignment = UIStackViewAlignment.Center,
            Spacing = 10,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        blur.ContentView.AddSubview(stack);
        window.AddSubview(blur);

        NSLayoutConstraint.ActivateConstraints(
        [
            blur.CenterXAnchor.ConstraintEqualTo(window.CenterXAnchor),
            blur.CenterYAnchor.ConstraintEqualTo(window.CenterYAnchor),
            blur.WidthAnchor.ConstraintGreaterThanOrEqualTo(160),
            blur.WidthAnchor.ConstraintLessThanOrEqualTo(260),
            stack.TopAnchor.ConstraintEqualTo(blur.ContentView.TopAnchor, 24),
            stack.BottomAnchor.ConstraintEqualTo(blur.ContentView.BottomAnchor, -22),
            stack.LeadingAnchor.ConstraintEqualTo(blur.ContentView.LeadingAnchor, 22),
            stack.TrailingAnchor.ConstraintEqualTo(blur.ContentView.TrailingAnchor, -22),
        ]);

        if (OperatingSystem.IsIOS())
            new UINotificationFeedbackGenerator().NotificationOccurred(UINotificationFeedbackType.Success);
        UIAccessibility.PostNotification(UIAccessibilityPostNotification.Announcement, new Foundation.NSString(message));

        UIView.AnimateNotify(0.35, 0, 0.7f, 0.5f, UIViewAnimationOptions.CurveEaseOut, () =>
        {
            blur.Alpha = 1;
            blur.Transform = CGAffineTransform.MakeIdentity();
        }, _ => UIView.Animate(0.3, Duration.TotalSeconds, UIViewAnimationOptions.CurveEaseIn, () =>
        {
            blur.Alpha = 0;
            blur.Transform = CGAffineTransform.MakeScale(0.95f, 0.95f);
        }, blur.RemoveFromSuperview));
    }
#endif
}
