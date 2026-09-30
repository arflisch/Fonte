using Fonte.Core.Models;

namespace Fonte.Services;

/// <summary>Colours given to the muscle groups, and small feedbacks (haptics, vibration).</summary>
public static class Palette
{
    public static readonly Color Primary = Color.FromArgb("#534AB7");

    public static Color Muscle(MuscleGroup muscle) => muscle switch
    {
        MuscleGroup.Chest => Color.FromArgb("#E24B4A"),
        MuscleGroup.Back => Color.FromArgb("#1D9E75"),
        MuscleGroup.Shoulders => Color.FromArgb("#378ADD"),
        MuscleGroup.Arms => Color.FromArgb("#D4537E"),
        MuscleGroup.Legs => Color.FromArgb("#534AB7"),
        MuscleGroup.Core => Color.FromArgb("#BA7517"),
        _ => Color.FromArgb("#639922"),
    };

    public static Color Soft(Color color) => color.WithAlpha(0.14f);

    /// <summary>Gradient used behind white text on an exercise's hero card.</summary>
    public static Brush HeroBrush(Color color) => new LinearGradientBrush(
        [new GradientStop(color.AddLuminosity(-0.02f), 0f), new GradientStop(color.AddLuminosity(-0.16f), 1f)],
        new Point(0, 0), new Point(1, 1));

    public static void Haptic()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (Exception)
        {
            // Haptics are a nicety: never let a missing capability break an action.
        }
    }

    /// <summary>Longer buzz, felt in a pocket: the rest is over.</summary>
    public static void Vibrate()
    {
        try
        {
            Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(500));
        }
        catch (Exception)
        {
            // Computers don't vibrate.
        }
    }
}
