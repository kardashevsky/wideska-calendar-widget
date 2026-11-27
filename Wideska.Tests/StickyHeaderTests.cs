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
        await TestWheelBurstsAsync(app, timeline, viewer);
        var frameClock = System.Diagnostics.Stopwatch.StartNew();
        var frameTimes = new List<double>();
        var scrollTimes = new List<double>();
        TimeSpan? previousRenderingTime = null;
        void RecordFrame(object? sender, EventArgs e)
        {
            if (e is RenderingEventArgs rendering)
            {
                if (rendering.RenderingTime == previousRenderingTime) return;
                previousRenderingTime = rendering.RenderingTime;
            }
            frameTimes.Add(frameClock.Elapsed.TotalMilliseconds);
        }
        void RecordScroll(object sender, ScrollChangedEventArgs e) { if (e.VerticalChange != 0) scrollTimes.Add(frameClock.Elapsed.TotalMilliseconds); }
        CompositionTarget.Rendering += RecordFrame;
        viewer.ScrollChanged += RecordScroll;
        try
        {
            for (int i = 0; i < 4; i++)
            {
                timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                    { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                await Task.Delay(45);
            }
            await Task.Delay(450);
        }
        finally { CompositionTarget.Rendering -= RecordFrame; viewer.ScrollChanged -= RecordScroll; }
        var frameGaps = frameTimes.Zip(frameTimes.Skip(1), (a, b) => b - a).Where(g => g > 1).Order().ToArray();
        Console.WriteLine($"Scroll frame sample: frames={frameTimes.Count}, offset updates={scrollTimes.Count}, median gap={(frameGaps.Length == 0 ? 0 : frameGaps[frameGaps.Length / 2]):F1} ms, maximum gap={frameGaps.DefaultIfEmpty().Max():F1} ms");
        viewer.ScrollToVerticalOffset(savedOffset);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        foreach (int delta in new[] { -120, 120, -240, 60 })
        {
            timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            if (SystemParameters.ClientAreaAnimation)
            {
                Check(timeline.IsScrollAnimating, "Wheel input must start frame-driven scrolling");
                await Task.Delay(65);
                Check(Math.Abs(viewer.VerticalOffset - savedOffset) < Math.Abs(delta / 120d * 72),
                    "Smooth scrolling must have an intermediate position instead of jumping to its target");
            }
            await Task.Delay(380);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            double settledOffset = viewer.VerticalOffset;
            Check(Math.Abs(settledOffset - (savedOffset - delta / 120d * 72)) < .5,
                "Wheel travel must support both directions, high-resolution and multi-detent input");
            Check(!timeline.IsScrollAnimating, "Rendering callbacks must stop after settling");
            await Task.Delay(220);
            Check(Math.Abs(viewer.VerticalOffset - settledOffset) < .5,
                "Settled scrolling must not apply a second delayed movement");
            savedOffset = settledOffset;
        }
        for (int i = 0; i < 3; i++)
            timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        await Task.Delay(400);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(Math.Abs(viewer.VerticalOffset - savedOffset - 216) < .5,
            "Rapid wheel events must accumulate without losing distance");
        timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        await Task.Delay(400);
        Check(Math.Abs(viewer.VerticalOffset - savedOffset - 144) < .5,
            "Reversing direction must immediately discard the previous forward target");
        timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        viewer.ScrollToVerticalOffset(500);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(240);
        Check(!timeline.IsScrollAnimating && Math.Abs(viewer.VerticalOffset - 500) < .5,
            "Manual scrollbar input must cancel wheel animation without a later pullback");
        timeline.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        timeline.Visibility = Visibility.Hidden;
        Check(!timeline.IsScrollAnimating, "Hiding a timeline must release its static rendering subscription");
        timeline.Visibility = Visibility.Visible;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SavePreview(window, "sticky-day-header", true);
    }
}
