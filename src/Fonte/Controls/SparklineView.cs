namespace Fonte.Controls;

/// <summary>Smooth line chart with a fading fill, used to show how a balance evolved.</summary>
public sealed class SparklineView : GraphicsView
{
    public static readonly BindableProperty PointsProperty =
        BindableProperty.Create(nameof(Points), typeof(IReadOnlyList<double>), typeof(SparklineView), null, propertyChanged: Redraw);

    public static readonly BindableProperty LineColorProperty =
        BindableProperty.Create(nameof(LineColor), typeof(Color), typeof(SparklineView), Colors.White, propertyChanged: Redraw);

    public SparklineView()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        Drawable = new SparklineDrawable(this);
    }

    public IReadOnlyList<double>? Points
    {
        get => (IReadOnlyList<double>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((SparklineView)bindable).Invalidate();

    private sealed class SparklineDrawable(SparklineView owner) : IDrawable
    {
        private const float LineWidth = 2.5f;

        public void Draw(ICanvas canvas, RectF rect)
        {
            var values = owner.Points;
            if (values is null || values.Count == 0 || rect.Width <= 0)
                return;

            // A single point is drawn as a flat line so the chart never looks broken.
            var series = values.Count == 1 ? [values[0], values[0]] : values;
            var min = series.Min();
            var max = series.Max();
            var top = rect.Top + LineWidth * 2;
            var usableHeight = rect.Height * 0.72f;
            var left = rect.Left;
            var width = rect.Width;

            var points = series.Select((v, i) => new PointF(
                left + width * i / (series.Count - 1),
                max - min < 0.005 ? top + usableHeight / 2 : top + usableHeight * (float)(1 - (v - min) / (max - min))))
                .ToList();

            var line = BuildSmoothPath(points);
            var fill = BuildSmoothPath(points);
            fill.LineTo(points[^1].X, rect.Bottom);
            fill.LineTo(points[0].X, rect.Bottom);
            fill.Close();

            var color = owner.LineColor;
            canvas.SetFillPaint(
                new LinearGradientPaint(
                    [new PaintGradientStop(0, color.WithAlpha(0.35f)), new PaintGradientStop(1, color.WithAlpha(0f))],
                    new Point(0, 0), new Point(0, 1)),
                rect);
            canvas.FillPath(fill);

            canvas.StrokeColor = color;
            canvas.StrokeSize = LineWidth;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(line);
        }

        /// <summary>Catmull-Rom spline converted to cubic Bézier segments.</summary>
        private static PathF BuildSmoothPath(IReadOnlyList<PointF> p)
        {
            var path = new PathF();
            path.MoveTo(p[0]);
            for (var i = 0; i < p.Count - 1; i++)
            {
                var p0 = p[Math.Max(i - 1, 0)];
                var p1 = p[i];
                var p2 = p[i + 1];
                var p3 = p[Math.Min(i + 2, p.Count - 1)];
                path.CurveTo(
                    p1.X + (p2.X - p0.X) / 6f, p1.Y + (p2.Y - p0.Y) / 6f,
                    p2.X - (p3.X - p1.X) / 6f, p2.Y - (p3.Y - p1.Y) / 6f,
                    p2.X, p2.Y);
            }
            return path;
        }
    }
}
