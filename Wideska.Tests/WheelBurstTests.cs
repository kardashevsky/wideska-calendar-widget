using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wideska.Controls;

internal static partial class Program
{
    private static async Task TestWheelBurstsAsync(Application app, DayTimeline timeline, ScrollViewer viewer)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        double originalOffset = viewer.VerticalOffset;
        viewer.ScrollToVerticalOffset(200);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        void Wheel(int delta) => timeline.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        for (int i = 0; i < 4; i++) Wheel(-120);
        // Reproduce wheel input arriving after a frame submits its offset but before WPF applies it.
        Thread.Sleep(25);
        typeof(DayTimeline).GetMethod("OnScrollFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(timeline, [null, EventArgs.Empty]);
        var presenter = (System.Windows.Controls.Primitives.IScrollInfo)viewer.Template.FindName("PART_ScrollContentPresenter", viewer);
        viewer.UpdateLayout();
        Check(presenter.VerticalOffset > 200,
            "A render frame must update the scroll presenter before the dispatcher processes queued viewer commands");
        Wheel(-120);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(timeline.IsScrollAnimating, "The fifth wheel input must not cancel a pending animation frame");
        await Task.Delay(450);
        Check(Math.Abs(viewer.VerticalOffset - 560) < .5,
            "Five wheel inputs overlapping a pending frame must preserve their complete travel");
        foreach (int delta in new[] { 120, -120 })
        {
            viewer.ScrollToVerticalOffset(delta > 0 ? 1000 : 200);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            double startOffset = viewer.VerticalOffset;
            for (int i = 0; i < 8; i++)
            {
                Wheel(delta);
                await Task.Delay(i % 2 == 0 ? 24 : 45);
                Check(timeline.IsScrollAnimating, "An eight-input wheel burst must remain animated between inputs");
            }
            await Task.Delay(450);
            double target = Math.Clamp(startOffset - delta / 120d * 8 * 72, 0, viewer.ScrollableHeight);
            Check(!timeline.IsScrollAnimating && Math.Abs(viewer.VerticalOffset - target) < .5,
                "Long wheel bursts must settle at their full target in both directions");
        }
        viewer.ScrollToVerticalOffset(originalOffset);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        ScrollChangedEventArgs? delayedAcknowledgement = null;
        void CaptureAcknowledgement(object sender, ScrollChangedEventArgs e) { if (e.VerticalChange != 0) delayedAcknowledgement = e; }
        viewer.ScrollChanged += CaptureAcknowledgement;
        viewer.ScrollToVerticalOffset(originalOffset + 2);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        viewer.ScrollChanged -= CaptureAcknowledgement;
        Wheel(-120);
        await Task.Delay(35);
        typeof(DayTimeline).GetMethod("OnScrollChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(timeline, [viewer, delayedAcknowledgement!]);
        Check(timeline.IsScrollAnimating, "Delayed offset acknowledgements must not be mistaken for manual scrollbar input");
        await Task.Delay(450);
        Wheel(-120);
        viewer.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(viewer), Environment.TickCount, Key.PageDown)
            { RoutedEvent = UIElement.PreviewKeyDownEvent });
        Check(!timeline.IsScrollAnimating, "Keyboard scrolling must explicitly take control from the wheel animation");
        Wheel(-120);
        var scrollbar = (System.Windows.Controls.Primitives.ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
        scrollbar.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.PreviewMouseDownEvent, Handled = true });
        Check(!timeline.IsScrollAnimating, "Scrollbar pointer input must cancel animation even when its event is handled by the control");
        viewer.ScrollToVerticalOffset(originalOffset);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
