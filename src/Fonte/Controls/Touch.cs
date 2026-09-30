using System.Windows.Input;

namespace Fonte.Controls;

/// <summary>
/// Makes any view tappable with a brief fade as visual feedback:
/// <c>c:Touch.Command="{Binding SaveCommand}"</c>.
/// </summary>
public static class Touch
{
    private const string Marker = "Fonte.Touch";

    public static readonly BindableProperty CommandProperty =
        BindableProperty.CreateAttached("Command", typeof(ICommand), typeof(Touch), null, propertyChanged: OnCommandChanged);

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.CreateAttached("CommandParameter", typeof(object), typeof(Touch), null);

    public static ICommand? GetCommand(BindableObject view) => (ICommand?)view.GetValue(CommandProperty);

    public static void SetCommand(BindableObject view, ICommand? value) => view.SetValue(CommandProperty, value);

    public static object? GetCommandParameter(BindableObject view) => view.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(BindableObject view, object? value) => view.SetValue(CommandParameterProperty, value);

    private static void OnCommandChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view || view.GestureRecognizers.OfType<TapGestureRecognizer>().Any(g => g.ClassId == Marker))
            return;

        var tap = new TapGestureRecognizer { ClassId = Marker };
        tap.Tapped += (_, _) =>
        {
            var command = GetCommand(view);
            var parameter = GetCommandParameter(view);
            if (command?.CanExecute(parameter) != true)
                return;

            view.CancelAnimations();
            view.Opacity = 0.55;
            _ = view.FadeToAsync(1, 220, Easing.CubicOut);
            command.Execute(parameter);
        };
        view.GestureRecognizers.Add(tap);

        // With a mouse or trackpad, show what is clickable. Scale rather than opacity, which some rows bind.
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
        {
            var hover = new PointerGestureRecognizer();
            hover.PointerEntered += (_, _) => _ = view.ScaleToAsync(1.015, 120, Easing.CubicOut);
            hover.PointerExited += (_, _) => _ = view.ScaleToAsync(1, 120, Easing.CubicOut);
            view.GestureRecognizers.Add(hover);
        }
    }
}
