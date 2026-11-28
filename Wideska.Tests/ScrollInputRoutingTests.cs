using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Controls;

internal static partial class Program
{
    private static async Task TestScrollInputRoutingAsync(Application app, Window window, DayTimeline timeline, ScrollViewer viewer)
    {
        double headerHeight = ((DrawingVisual)VisualTreeHelper.GetChild(timeline, 1)).ContentBounds.Height;
        for (double y = headerHeight + 12; y < viewer.ViewportHeight - 12; y += 13)
        {
            var point = timeline.TransformToAncestor(window).Transform(new Point(DayTimeline.TimeGutterWidth + 35, viewer.VerticalOffset + y));
            var target = window.InputHitTest(point) as DependencyObject;
            while (target is not null && target != timeline) target = VisualTreeHelper.GetParent(target);
            Check(target == timeline, "Every empty calendar slot must route wheel input through the timeline, including unshaded hourly bands");
        }
        if (!SystemParameters.ClientAreaAnimation) return;
        UIElement Target(double y)
        {
            var point = timeline.TransformToAncestor(window).Transform(new Point(DayTimeline.TimeGutterWidth + 35, viewer.VerticalOffset + y));
            return (UIElement)window.InputHitTest(point);
        }
        void Wheel(double y, int delta)
        {
            var target = Target(y);
            var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            target.RaiseEvent(preview);
            if (!preview.Handled) target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = UIElement.MouseWheelEvent });
        }
        viewer.ScrollToVerticalOffset(100);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        double pointerY = Math.Min(headerHeight + 100, viewer.ViewportHeight - 30);
        Wheel(pointerY, -1200);
        await Task.Delay(90);
        double lastOffset = timeline.DisplayedVerticalOffset;
        for (int input = 0; input < 8; input++)
        {
            Wheel(pointerY, 120);
            for (int sample = 0; sample < 12; sample++)
            {
                await Task.Delay(15);
                double offset = timeline.DisplayedVerticalOffset;
                Check(offset <= lastOffset + .5, "Slow upward wheel input after a fast downward burst must never jump downward");
                lastOffset = offset;
            }
        }
        await Task.Delay(400);
    }
}
