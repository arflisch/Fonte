using System.Numerics;

namespace Fonte.Controls;

/// <summary>Draws one of the <see cref="Icons"/> as a themeable vector stroke.</summary>
public sealed class IconView : GraphicsView
{
    public static readonly BindableProperty DataProperty =
        BindableProperty.Create(nameof(Data), typeof(string), typeof(IconView), string.Empty, propertyChanged: Redraw);

    public static readonly BindableProperty ColorProperty =
        BindableProperty.Create(nameof(Color), typeof(Color), typeof(IconView), Colors.Black, propertyChanged: Redraw);

    public static readonly BindableProperty StrokeWidthProperty =
        BindableProperty.Create(nameof(StrokeWidth), typeof(double), typeof(IconView), 2.0, propertyChanged: Redraw);

    public IconView()
    {
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        WidthRequest = 22;
        HeightRequest = 22;
        Drawable = new IconDrawable(this);
    }

    public string Data
    {
        get => (string)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) =>
        ((IconView)bindable).Invalidate();

    private sealed class IconDrawable(IconView owner) : IDrawable
    {
        private const float Grid = 24f;

        public void Draw(ICanvas canvas, RectF rect)
        {
            if (string.IsNullOrEmpty(owner.Data) || rect.Width <= 0 || rect.Height <= 0)
                return;

            var scale = Math.Min(rect.Width, rect.Height) / Grid;
            var offsetX = rect.X + (rect.Width - Grid * scale) / 2f;
            var offsetY = rect.Y + (rect.Height - Grid * scale) / 2f;

            var path = PathBuilder.Build(owner.Data);
            path.Transform(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(offsetX, offsetY));

            canvas.StrokeColor = owner.Color;
            canvas.StrokeSize = (float)owner.StrokeWidth * scale;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(path);
        }
    }
}
