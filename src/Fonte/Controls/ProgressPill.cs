namespace Fonte.Controls;

/// <summary>Rounded progress bar that looks identical on every platform.</summary>
public sealed class ProgressPill : GraphicsView
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressPill), 0.0, propertyChanged: Redraw);

    public static readonly BindableProperty FillColorProperty =
        BindableProperty.Create(nameof(FillColor), typeof(Color), typeof(ProgressPill), Colors.Black, propertyChanged: Redraw);

    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(ProgressPill), Colors.LightGray, propertyChanged: Redraw);

    public ProgressPill()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        HeightRequest = 6;
        Drawable = new PillDrawable(this);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((ProgressPill)bindable).Invalidate();

    private sealed class PillDrawable(ProgressPill owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var radius = rect.Height / 2f;
            canvas.FillColor = owner.TrackColor;
            canvas.FillRoundedRectangle(rect, radius);

            var progress = Math.Clamp(owner.Progress, 0, 1);
            if (progress <= 0)
                return;

            var width = Math.Max(rect.Height, rect.Width * (float)progress);
            canvas.FillColor = owner.FillColor;
            canvas.FillRoundedRectangle(new RectF(rect.X, rect.Y, width, rect.Height), radius);
        }
    }
}
