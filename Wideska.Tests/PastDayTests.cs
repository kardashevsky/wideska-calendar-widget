using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wideska.Controls;
using Wideska.ViewModels;

internal static partial class Program
{
    private static void TestPastDayRendering()
    {
        var date = new DateTime(2026, 11, 27);
        var timed = new EventItem("Focus", "10:00 - 12:00", "#38BDF8", "Focus", StartMinute: 600, EndMinute: 720);
        var allDay = new EventItem("Reminder", "All day", "#38BDF8", "Reminder", AllDay: true);
        var timeline = new DayTimeline
        {
            Width = 684, Height = 1800, Foreground = Brushes.White,
            CurrentTime = new DateTimeOffset(2026, 11, 27, 12, 30, 0, TimeSpan.Zero), TimeZoneId = "UTC",
            Days = [new(date.AddDays(-1), true, false, [timed, allDay]) { IsPast = true },
                new(date, true, true, [timed, allDay]), new(date.AddDays(1), true, false, [timed, allDay])]
        };
        timeline.Measure(new Size(684, 1800));
        timeline.Arrange(new Rect(0, 0, 684, 1800));
        timeline.UpdateLayout();
        var bitmap = new RenderTargetBitmap(684, 1800, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(timeline);
        byte[] Pixel(int x, int y) { var value = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), value, 4, 0); return value; }
        int eventY = (int)(76 + (1800 - 76 - DayTimeline.TimelineBottomPadding) * 690d / 1440);
        var past = Pixel(184, eventY); var today = Pixel(384, eventY); var future = Pixel(584, eventY);
        Check(Pixel(30, 12)[3] == 0, "The unused header above the time gutter must remain transparent");
        Check(past[0] < future[0] && past[1] < future[1], "Past event cards must render darker than future cards");
        Check(today.SequenceEqual(future), "Today's event colors must remain unchanged");
        Check(ReferenceEquals(timeline.EventAt(new Point(184, eventY)), timed)
            && ReferenceEquals(timeline.EventAt(new Point(184, 48)), allDay),
            "Shading must retain timed and all-day event hit targets");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create("artifacts/past-day-shading.png"); encoder.Save(output);
    }
}
