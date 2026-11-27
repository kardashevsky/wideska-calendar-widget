using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Controls;

internal static partial class Program
{
    private static async Task TestStickyHeaderAsync(Application app, Window window)
    {
        var timeline = (DayTimeline)window.FindName("Timeline");
        DependencyObject? parent = VisualTreeHelper.GetParent(timeline);
        while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        var viewer = (ScrollViewer)parent!;
        var header = (DrawingVisual)VisualTreeHelper.GetChild(timeline,1);
        double initialTop = header.TransformToAncestor(window).Transform(new Point()).Y;
        viewer.ScrollToVerticalOffset(600);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        double afterTop = header.TransformToAncestor(window).Transform(new Point()).Y;
        Check(viewer.VerticalOffset > 0 && Math.Abs(afterTop - initialTop) < .5, "Day/date and all-day header must stay at the same screen position while hours scroll");
        Check(timeline.Clip is RectangleGeometry viewportClip && Math.Abs(viewportClip.Rect.Top - viewer.VerticalOffset) < .5 && Math.Abs(viewportClip.Rect.Height - viewer.ViewportHeight) < .5, "Timeline visuals must be clipped to the viewport so scrolled events cannot leak above the header");
        double headerHeight = header.ContentBounds.Height;
        var allDay = timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, viewer.VerticalOffset + 48));
        Check(allDay?.AllDay == true, "Pinned all-day cards must retain their event hit targets after scrolling");
        var hidden = timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, viewer.VerticalOffset + headerHeight - 2));
        Check(hidden is null, "Events scrolling under the pinned header must not receive clicks through it");
        Check(timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, 900)) is null, "Empty hourly slots must not be treated as event hit targets");
        double eventY = headerHeight + 655d / 1440 * (timeline.ActualHeight - headerHeight - 16);
        Check(timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, eventY))?.Title == "Planning session", "Timed event hit targets must remain aligned after scrolling");
        SavePreview(window, "sticky-day-header", true);
    }
}
