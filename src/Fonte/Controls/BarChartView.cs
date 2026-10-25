namespace Fonte.Controls;

/// <summary>Rounded bars around a zero line, with a short label under each, and an optional dashed target line.</summary>
public sealed class BarChartView : GraphicsView
{
    public static readonly BindableProperty ValuesProperty =
        BindableProperty.Create(nameof(Values), typeof(IReadOnlyList<double>), typeof(BarChartView), null, propertyChanged: Redraw);

    public static readonly BindableProperty LabelsProperty =
        BindableProperty.Create(nameof(Labels), typeof(IReadOnlyList<string>), typeof(BarChartView), null, propertyChanged: Redraw);

    public static readonly BindableProperty PositiveColorProperty =
        BindableProperty.Create(nameof(PositiveColor), typeof(Color), typeof(BarChartView), Color.FromArgb("#10B981"), propertyChanged: Redraw);

    public static readonly BindableProperty NegativeColorProperty =
        BindableProperty.Create(nameof(NegativeColor), typeof(Color), typeof(BarChartView), Color.FromArgb("#F43F5E"), propertyChanged: Redraw);

    public static readonly BindableProperty LabelColorProperty =
        BindableProperty.Create(nameof(LabelColor), typeof(Color), typeof(BarChartView), Colors.Gray, propertyChanged: Redraw);

    public static readonly BindableProperty TargetProperty =
        BindableProperty.Create(nameof(Target), typeof(double), typeof(BarChartView), 0.0, propertyChanged: Redraw);

    public BarChartView()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        Drawable = new BarDrawable(this);
    }

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IReadOnlyList<string>? Labels
    {
        get => (IReadOnlyList<string>?)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    public Color PositiveColor
    {
        get => (Color)GetValue(PositiveColorProperty);
        set => SetValue(PositiveColorProperty, value);
    }

    public Color NegativeColor
    {
        get => (Color)GetValue(NegativeColorProperty);
        set => SetValue(NegativeColorProperty, value);
    }

    public Color LabelColor
    {
        get => (Color)GetValue(LabelColorProperty);
        set => SetValue(LabelColorProperty, value);
    }

    /// <summary>Value drawn as a dashed line (a daily calorie target); none when 0.</summary>
    public double Target
    {
        get => (double)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((BarChartView)bindable).Invalidate();

    private sealed class BarDrawable(BarChartView owner) : IDrawable
    {
        private const float LabelHeight = 18f;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var values = owner.Values;
            if (values is null || values.Count == 0 || rect.Width <= 0)
                return;

            var chart = new RectF(rect.X, rect.Y + 4, rect.Width, rect.Height - LabelHeight - 8);
            var max = Math.Max(Math.Max(values.Max(), owner.Target * 1.1), 0);
            var min = Math.Min(values.Min(), 0);
            var span = max - min < 0.005 ? 1 : max - min;
            float Y(double v) => chart.Top + chart.Height * (float)((max - v) / span);
            var zero = Y(0);

            var slot = chart.Width / values.Count;
            var barWidth = Math.Min(slot * 0.56f, 22f);

            canvas.StrokeColor = owner.LabelColor.WithAlpha(0.35f);
            canvas.StrokeSize = 1;
            canvas.DrawLine(chart.Left, zero, chart.Right, zero);
            if (owner.Target > 0)
            {
                canvas.StrokeDashPattern = [4, 4];
                canvas.DrawLine(chart.Left, Y(owner.Target), chart.Right, Y(owner.Target));
                canvas.StrokeDashPattern = null;
            }

            for (var i = 0; i < values.Count; i++)
            {
                var x = chart.Left + slot * i + (slot - barWidth) / 2;
                var top = Math.Min(Y(values[i]), zero);
                var height = Math.Max(Math.Abs(Y(values[i]) - zero), values[i] == 0 ? 0 : 2f);
                if (height > 0)
                {
                    canvas.FillColor = values[i] >= 0 ? owner.PositiveColor : owner.NegativeColor;
                    canvas.FillRoundedRectangle(x, top, barWidth, height, Math.Min(6f, barWidth / 2));
                }

                if (owner.Labels is { } labels && i < labels.Count)
                {
                    canvas.FontColor = owner.LabelColor;
                    canvas.FontSize = 11;
                    canvas.DrawString(labels[i], chart.Left + slot * i, rect.Bottom - LabelHeight, slot, LabelHeight,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }
        }
    }
}
