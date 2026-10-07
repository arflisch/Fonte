#if IOS || ANDROID || MACCATALYST
using Microsoft.Maui.Graphics.Platform;
#endif
using Fonte.Core.Training;
using Fonte.Localization;

namespace Fonte.Services;

/// <summary>The month's training as an image to share (Fonte Pro): totals, records, streak and calendar.</summary>
public static class MonthCard
{
    private const int Width = 1080;
    private const int Height = 1350;
    private const float Margin = 90;

    // The app's own fonts, registered at launch.
    private static readonly Microsoft.Maui.Graphics.Font Bold = new("OpenSans-Semibold");
    private static readonly Microsoft.Maui.Graphics.Font Regular = new("OpenSans-Regular");

    /// <summary>Draws the card and returns the path of the PNG file.</summary>
    public static async Task<string> RenderAsync(MonthReport report, int regularWeeks)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"Fonte-{report.Year}-{report.Month:00}.png");
#if IOS || ANDROID || MACCATALYST
        using var context = new PlatformBitmapExportService().CreateContext(Width, Height);
        Draw(context.Canvas, report, regularWeeks);
        await using var file = File.Create(path);
        await context.Image.SaveAsync(file);
#else
        await Task.CompletedTask;
#endif
        return path;
    }

    private static void Draw(ICanvas canvas, MonthReport report, int regularWeeks)
    {
        var accent = AccentTheme.Current;
        var culture = Loc.Culture;
        var white = Colors.White;

        // Background: the app's gradient, with two soft circles.
        canvas.SetFillPaint(
            new LinearGradientPaint(
                [new PaintGradientStop(0, accent.Deep), new PaintGradientStop(0.55f, accent.Base), new PaintGradientStop(1, accent.Light)],
                new Point(0, 0), new Point(1, 1)),
            new RectF(0, 0, Width, Height));
        canvas.FillRectangle(0, 0, Width, Height);
        canvas.FillColor = white.WithAlpha(0.08f);
        canvas.FillCircle(Width - 60, 120, 330);
        canvas.FillCircle(40, Height - 80, 240);

        canvas.Font = Bold;
        Text(canvas, "FONTE", Margin, 96, 34, white.WithAlpha(0.75f));
        var month = new DateTime(report.Year, report.Month, 1);
        Text(canvas, Loc.Capitalize(month.ToString("MMMM yyyy", culture)), Margin, 150, 78, white);
        canvas.Font = Regular;
        Text(canvas, Loc.Get("MonthCard_Subtitle"), Margin, 250, 38, white.WithAlpha(0.85f));

        // Four figures, two by two.
        var records = report.Workouts.Sum(w => w.Records.Count);
        (string Value, string Label)[] figures =
        [
            (report.Workouts.Count.ToString(culture), Loc.Get("History_Workouts")),
            (report.Workouts.Count == 0 ? "0" : Loc.Duration(report.Duration), Loc.Get("History_Time")),
            ($"{Loc.Number(Math.Round(report.Volume / 1000, 1))} t", Loc.Get("Summary_Volume")),
            (records.ToString(culture), Loc.Get("Summary_Records")),
        ];
        const float gap = 30;
        var cellWidth = (Width - 2 * Margin - gap) / 2;
        const float cellHeight = 170;
        for (var i = 0; i < figures.Length; i++)
        {
            var x = Margin + (i % 2) * (cellWidth + gap);
            var y = 340 + (i / 2) * (cellHeight + gap);
            canvas.FillColor = white.WithAlpha(0.16f);
            canvas.FillRoundedRectangle(x, y, cellWidth, cellHeight, 36);
            canvas.Font = Bold;
            Text(canvas, figures[i].Value, x + 34, y + 26, 64, white);
            canvas.Font = Regular;
            Text(canvas, figures[i].Label, x + 34, y + 108, 32, white.WithAlpha(0.85f));
        }

        canvas.Font = Bold;
        Text(canvas, Loc.Format("MonthCard_Streak", regularWeeks), Margin, 740, 36, white);

        // The month's calendar: a filled dot for each day trained.
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)month.DayOfWeek - (int)firstDay + 7) % 7;
        var days = DateTime.DaysInMonth(report.Year, report.Month);
        var trained = report.Days;
        var column = (Width - 2 * Margin) / 7;
        const float rowHeight = 74;
        const float top = 820;
        for (var day = 1; day <= days; day++)
        {
            var cell = offset + day - 1;
            var cx = Margin + column * (cell % 7) + column / 2;
            var cy = top + rowHeight * (cell / 7) + rowHeight / 2;
            var done = trained.Contains(new DateTime(report.Year, report.Month, day));
            canvas.FillColor = done ? white : white.WithAlpha(0.14f);
            canvas.FillCircle(cx, cy, 28);
            canvas.Font = Bold;
            canvas.FontSize = 24;
            canvas.FontColor = done ? accent.Deep : white.WithAlpha(0.8f);
            canvas.DrawString(day.ToString(culture), cx - 30, cy - 15, 60, 30, HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        canvas.Font = Regular;
        Text(canvas, Loc.Get("MonthCard_Footer"), Margin, Height - 70, 28, white.WithAlpha(0.7f));
    }

    private static void Text(ICanvas canvas, string text, float x, float y, float size, Color color)
    {
        canvas.FontSize = size;
        canvas.FontColor = color;
        canvas.DrawString(text, x, y, Width - x - Margin, size * 1.4f, HorizontalAlignment.Left, VerticalAlignment.Top);
    }
}
