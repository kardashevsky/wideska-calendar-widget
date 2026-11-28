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
        double savedOffset = viewer.VerticalOffset;
        await ((Wideska.ViewModels.MainViewModel)window.DataContext).RefreshAsync();
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Math.Abs(viewer.VerticalOffset - savedOffset) < .5, "Calendar synchronization must preserve the manually selected scroll position");
        var viewModel = (Wideska.ViewModels.MainViewModel)window.DataContext;
        viewModel.NextCommand.Execute(null);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        viewModel.PreviousCommand.Execute(null);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Math.Abs(viewer.VerticalOffset - savedOffset) < .5, "Navigating away and back must preserve vertical scroll instead of jumping to the current time");
        double afterTop = header.TransformToAncestor(window).Transform(new Point()).Y;
        Check(viewer.VerticalOffset > 0 && Math.Abs(afterTop - initialTop) < .5, "Day/date and all-day header must stay at the same screen position while hours scroll");
        Check(timeline.Clip is RectangleGeometry viewportClip && Math.Abs(viewportClip.Rect.Top - viewer.VerticalOffset) < .5 && Math.Abs(viewportClip.Rect.Height - viewer.ViewportHeight) < .5, "Timeline visuals must be clipped to the viewport so scrolled events cannot leak above the header");
        double headerHeight = header.ContentBounds.Height;
        var allDay = timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, viewer.VerticalOffset + 48));
        Check(allDay?.AllDay == true, "Pinned all-day cards must retain their event hit targets after scrolling");
        var hidden = timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, viewer.VerticalOffset + headerHeight - 2));
        Check(hidden is null, "Events scrolling under the pinned header must not receive clicks through it");
        Check(timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, 900)) is null, "Empty hourly slots must not be treated as event hit targets");
        double eventY = headerHeight + 655d / 1440 * (timeline.ActualHeight - headerHeight - Wideska.Controls.DayTimeline.TimelineBottomPadding);
        Check(timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, eventY))?.Title == "Planning session", "Timed event hit targets must remain aligned after scrolling");
        foreach (int delta in new[] { -120, 120 })
        {
            timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.MouseWheelEvent });
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            double settledOffset = viewer.VerticalOffset;
            Check(delta < 0 ? settledOffset > savedOffset : settledOffset < savedOffset,
                "Native wheel scrolling must immediately move in the requested direction");
            await Task.Delay(350);
            Check(Math.Abs(viewer.VerticalOffset - settledOffset) < .5,
                "Native scrolling must not add any delayed animation or snapping");
            savedOffset = settledOffset;
        }
        await TestVisualScrollingAsync(app, window, timeline, viewer);
        await TestScrollInputRoutingAsync(app, window, timeline, viewer);
        SavePreview(window, "sticky-day-header", true);
    }
}
