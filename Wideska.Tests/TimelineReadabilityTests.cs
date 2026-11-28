using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wideska.Controls;
using Wideska.ViewModels;

internal static partial class Program
{
    private static void TestTimelineReadability()
    {
        var halfHour = new EventItem("Focus", "09:00 \u2013 09:30", "#38BDF8", "Focus", StartMinute: 540, EndMinute: 570);
        var late = new EventItem("Rest", "23:30 \u2013 00:00", "#38BDF8", "Rest", StartMinute: 1410, EndMinute: 1440);
        var timeline = new DayTimeline
        {
            Width = 216, Height = DayTimeline.MinimumTimelineHeight, Foreground = Brushes.White,
            CurrentTime = new DateTimeOffset(2026, 11, 28, 12, 0, 0, TimeSpan.Zero), TimeZoneId = "UTC",
            Days = [new(new DateTime(2026, 11, 26), true, false,
                [halfHour, late, new("A", "", "#38BDF8", "A", AllDay: true),
                 new("B", "", "#38BDF8", "B", AllDay: true), new("C", "", "#38BDF8", "C", AllDay: true)]) { IsPast = true }]
        };
        timeline.Measure(new Size(timeline.Width, timeline.Height));
        timeline.Arrange(new Rect(0, 0, timeline.Width, timeline.Height));
        timeline.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)timeline.Width, (int)timeline.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(timeline);
        var body = ((DrawingVisual)VisualTreeHelper.GetChild(timeline, 0)).Drawing;
        IEnumerable<Drawing> Descendants(Drawing drawing)
        {
            yield return drawing;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var item in Descendants(child)) yield return item;
        }
        var drawings = Descendants(body).ToArray();
        string text = string.Concat(drawings.OfType<GlyphRunDrawing>().Select(d => new string(d.GlyphRun.Characters?.ToArray() ?? [])));
        Check(text.Contains("Focus") && text.Contains("09:00\u201309:30") && text.Contains("23:30\u201300:00"),
            "Half-hour cards must draw their complete start/end times even at the minimum column width and maximum all-day header height");
        var lineWidths = drawings.OfType<GeometryDrawing>().Where(d => d.Pen?.Brush == timeline.GridBrush || d.Pen?.Brush == timeline.AxisBrush)
            .Select(d => d.Pen!.Thickness).Distinct().ToArray();
        Check(lineWidths.Length == 1 && Math.Abs(lineWidths[0] - 1) < .001,
            "The time axis, hourly lines and day separators must all use the same one-pixel stroke");
        double header = ((DrawingVisual)VisualTreeHelper.GetChild(timeline, 1)).ContentBounds.Height;
        double lastMinute = header + (timeline.ActualHeight - header) * 1430 / 1440;
        Check(ReferenceEquals(timeline.EventAt(new Point(110, lastMinute)), late), "The last half-hour card must remain clickable through midnight");
        var shaded = drawings.OfType<GeometryDrawing>().Where(d => d.Brush == timeline.PastDayShade).ToArray();
        Check(shaded.Length == 1 && Math.Abs(shaded[0].Geometry.Bounds.Bottom - timeline.ActualHeight) < .01
            && DayTimeline.TimelineBottomPadding == 0, "Past-day shading must stop exactly at the final midnight boundary without a trailing strip");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create("artifacts/short-event-readability.png"); encoder.Save(file);
    }
}
