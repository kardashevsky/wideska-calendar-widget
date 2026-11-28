using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Controls;

internal static partial class Program
{
    private static async Task TestVisualScrollingAsync(Application app, Window window, DayTimeline timeline, ScrollViewer viewer)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        void Wheel(int delta) => timeline.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        viewer.ScrollToVerticalOffset(200);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        double initialOffset = viewer.VerticalOffset;
        var header = (DrawingVisual)VisualTreeHelper.GetChild(timeline, 1);
        double headerTop = header.TransformToAncestor(window).Transform(new Point()).Y;
        int layouts = timeline.LayoutPassCount, drawings = timeline.DrawPassCount, updates = 0;
        void RecordUpdate(object sender, ScrollChangedEventArgs e) { if (e.VerticalChange != 0) updates++; }
        viewer.ScrollChanged += RecordUpdate;
        try
        {
            for (int i = 0; i < 8; i++)
            {
                Wheel(-120);
                await Task.Delay(i % 2 == 0 ? 25 : 45);
                Check(timeline.IsScrollAnimating && timeline.DisplayedVerticalOffset > initialOffset,
                    "Continuous wheel input must keep the visual movement active");
                Check(Math.Abs(viewer.VerticalOffset - initialOffset) < .01,
                    "Animation frames must leave the layout scroll offset unchanged");
                Check(Math.Abs(header.TransformToAncestor(window).Transform(new Point()).Y - headerTop) < .5,
                    "The date and all-day header must remain pinned during visual scrolling");
                Check(timeline.EventAt(new Point(DayTimeline.TimeGutterWidth + 20, viewer.VerticalOffset + 48))?.AllDay == true,
                    "Pinned all-day hit targets must follow the displayed offset during animation");
            }
            Console.WriteLine($"Active visual scroll: arrangements={timeline.LayoutPassCount - layouts}, redraws={timeline.DrawPassCount - drawings}, native updates={updates}");
            Check(updates == 0 && timeline.LayoutPassCount == layouts && timeline.DrawPassCount <= drawings + 1,
                "A wheel burst must not arrange or redraw the calendar or issue layout scroll commands per frame");
            await Task.Delay(500);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(!timeline.IsScrollAnimating && Math.Abs(viewer.VerticalOffset - initialOffset - 576) < .5,
                "The final visual position must commit once to native scrolling");
            Check(updates <= 2 && Math.Abs(((TranslateTransform)((DrawingVisual)VisualTreeHelper.GetChild(timeline, 0)).Transform).Y) < .01,
                "Settling must clear the visual translation without a second scroll animation");
            Console.WriteLine($"Visual scroll burst: calendar arrangements={timeline.LayoutPassCount - layouts}, redraws={timeline.DrawPassCount - drawings}, native offset updates={updates}");
        }
        finally { viewer.ScrollChanged -= RecordUpdate; }
        Wheel(120);
        await Task.Delay(65);
        double reverseStart = timeline.DisplayedVerticalOffset;
        Wheel(-120);
        await Task.Delay(65);
        Check(timeline.DisplayedVerticalOffset > reverseStart, "Reversing the wheel must immediately change movement direction");
        var bar = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
        double visibleOffset = timeline.DisplayedVerticalOffset;
        Check(Math.Abs(bar.Value - visibleOffset) < .5, "The scrollbar thumb must track the visual position during movement");
        SavePreview(window, "smooth-scroll-active", true);
        bar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 500));
        Check(!timeline.IsScrollAnimating && Math.Abs(viewer.VerticalOffset - visibleOffset) < .5,
            "Taking the scrollbar must commit the visible position without jumping to an old layout offset");
        viewer.ScrollToVerticalOffset(500);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(350);
        Check(Math.Abs(viewer.VerticalOffset - 500) < .5, "Manual scrollbar changes must not be pulled back by animation");
        Wheel(-120);
        await Task.Delay(60);
        visibleOffset = timeline.DisplayedVerticalOffset;
        timeline.Visibility = Visibility.Hidden;
        Check(!timeline.IsScrollAnimating && Math.Abs(viewer.VerticalOffset - visibleOffset) < .5,
            "Hiding the timeline must release rendering callbacks and preserve its visible scroll position");
        timeline.Visibility = Visibility.Visible;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
