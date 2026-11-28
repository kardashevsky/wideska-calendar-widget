using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Controls;
using Wideska.Models;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    private static async Task TestIdleReturnAsync(Application app, string directory)
    {
        using var model = new MainViewModel(new LocalStore(Path.Combine(directory, "idle-return")), new FakeProvider());
        await model.InitializeAsync();
        model.UpdateCurrentTime(DateTimeOffset.UtcNow);
        var window = new Wideska.MainWindow(model, false) { Width = 720, Height = 660 };
        window.Show();
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var timeline = (DayTimeline)window.FindName("Timeline");
        DependencyObject? parent = VisualTreeHelper.GetParent(timeline);
        while (parent is not null && parent is not ScrollViewer) parent = VisualTreeHelper.GetParent(parent);
        var viewer = (ScrollViewer)parent!;
        double Target()
        {
            double header = ((DrawingVisual)VisualTreeHelper.GetChild(timeline, 1)).ContentBounds.Height;
            var today = model.Days.Single(d => d.Today);
            double minutes = CalendarTime.IndicatorMinutes(today.Date, model.CurrentTime, model.CalendarTimeZoneId)!.Value;
            return Math.Clamp(minutes / 1440 * (timeline.ActualHeight - header - DayTimeline.TimelineBottomPadding)
                - (viewer.ViewportHeight - header) / 3, 0, viewer.ScrollableHeight);
        }
        foreach (var view in new[] { CalendarView.SingleDay, CalendarView.ThreeDays, CalendarView.Workweek, CalendarView.Week })
        {
            model.View = view;
            model.TodayCommand.Execute(null);
            window.ResetIdleReturnTimer();
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            viewer.ScrollToVerticalOffset(Target() < viewer.ScrollableHeight / 2 ? viewer.ScrollableHeight : 0);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var foreground = GetForegroundWindow();
            await window.ReturnAfterIdleAsync();
            if (SystemParameters.ClientAreaAnimation)
            {
                Check(timeline.IsReturningToNow, "Idle return must use the slow visual-scroll animation in each time-grid view");
                await Task.Delay(180);
                Check(timeline.IsReturningToNow, "Automatic movement must not finish abruptly within the first 180 ms");
            }
            await Task.Delay(1850);
            Check(!timeline.IsScrollAnimating && Math.Abs(viewer.VerticalOffset - Target()) < 2,
                $"Idle return must place current time one third down the visible grid: view={view}, actual={viewer.VerticalOffset:F1}, expected={Target():F1}, animating={timeline.IsScrollAnimating}");
            var widgetHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Check(GetForegroundWindow() != widgetHandle || foreground == widgetHandle,
                "Returning to now must not activate or focus the widget");
        }
        viewer.ScrollToVerticalOffset(Target() < viewer.ScrollableHeight / 2 ? viewer.ScrollableHeight : 0);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await window.ReturnAfterIdleAsync();
        await Task.Delay(100);
        window.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        Check(!timeline.IsReturningToNow, "New widget input must cancel automatic movement immediately");
        double interruptedOffset = viewer.VerticalOffset;
        await Task.Delay(250);
        Check(Math.Abs(viewer.VerticalOffset - interruptedOffset) < .5, "Cancelled idle movement must not resume or pull the view back");
        model.OpenEventCommand.Execute(model.Days.SelectMany(d => d.Events).First());
        await window.ReturnAfterIdleAsync();
        Check(model.IsEventOpen && !timeline.IsReturningToNow, "Reading event details must suspend idle navigation");
        model.CloseEventCommand.Execute(null);
        model.SettingsOpen = true;
        await window.ReturnAfterIdleAsync();
        Check(model.SettingsOpen && !timeline.IsReturningToNow, "Editing settings must suspend idle navigation");
        model.SettingsOpen = false;
        foreach (var view in new[] { CalendarView.Month, CalendarView.Year })
        {
            model.View = view;
            model.NextCommand.Execute(null);
            window.ResetIdleReturnTimer();
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.ReturnAfterIdleAsync();
            Check(model.View == view && !model.ShouldShowToday && !timeline.IsReturningToNow,
                "Month and year views must return to the current period without changing their format or inventing a time scale");
        }
        model.View = CalendarView.SingleDay;
        model.TodayCommand.Execute(null);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        viewer.ScrollToVerticalOffset(Target() < viewer.ScrollableHeight / 2 ? viewer.ScrollableHeight : 0);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.ResetIdleReturnTimer();
        double bottom = viewer.VerticalOffset;
        await Task.Delay(4000);
        window.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        await Task.Delay(7000);
        Check(Math.Abs(viewer.VerticalOffset - bottom) < .5, "Widget input must restart the real ten-second inactivity countdown");
        await Task.Delay(5000);
        Check(Math.Abs(viewer.VerticalOffset - Target()) < 2 && !timeline.IsScrollAnimating,
            "The real idle timer must return to now ten seconds after the last widget input without requiring focus");
        window.Close();
        await model.ShutdownAsync();
    }
}
