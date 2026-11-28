using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Controls;
using Wideska.ViewModels;

internal static partial class Program
{
    private static async Task TestCarouselPerformanceAsync(Application app, Window window)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var model = (MainViewModel)window.DataContext;
        var timeline = (DayTimeline)window.FindName("Timeline");
        var content = (FrameworkElement)window.FindName("CalendarContent");
        var outgoing = (Image)window.FindName("OutgoingCalendar");
        var incoming = (Image)window.FindName("IncomingCalendar");
        var next = (Button)window.FindName("NextPeriodButton");
        next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, next));
        model.NextCommand.Execute(null);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(incoming.Source is { IsFrozen: true } && outgoing.Source is { IsFrozen: true },
            "Carousel motion must use two frozen snapshots");
        Check(content.Opacity == 0 && !content.IsHitTestVisible && !content.RenderTransform.HasAnimatedProperties,
            "The live calendar must remain stationary and noninteractive while its snapshots slide");
        int layouts = timeline.LayoutPassCount, drawings = timeline.DrawPassCount;
        await Task.Delay(100);
        Check(timeline.LayoutPassCount == layouts && timeline.DrawPassCount <= drawings + 1,
            "Horizontal slide frames must not arrange or redraw the live calendar");
        Console.WriteLine($"Carousel active frames: calendar arrangements={timeline.LayoutPassCount - layouts}, redraws={timeline.DrawPassCount - drawings}");
        SavePreview(window, "smooth-carousel-active", true);
        await Task.Delay(350);
        Check(incoming.Source is null && outgoing.Source is null && content.Opacity == 1 && content.IsHitTestVisible,
            "Finishing the carousel must release both snapshots and restore interactive content");
        model.PreviousCommand.Execute(null);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
