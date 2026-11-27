using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Models;
using Wideska.ViewModels;

internal static partial class Program
{
    private static async Task TestTodayNavigationAsync(Application app, Window window)
    {
        var model = (MainViewModel)window.DataContext;
        var button = (Button)window.FindName("TodayButton");
        var outgoing = (Image)window.FindName("OutgoingCalendar");
        var content = (FrameworkElement)window.FindName("CalendarContent");
        foreach (var view in new[] { CalendarView.SingleDay, CalendarView.Week, CalendarView.Month, CalendarView.Year })
        foreach (int direction in new[] { -1, 1 })
        {
            model.View = view;
            model.TodayCommand.Execute(null);
            (direction < 0 ? model.PreviousCommand : model.NextCommand).Execute(null);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(model.ShouldShowToday, "A different period must expose Show Today");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            if (SystemParameters.ClientAreaAnimation)
                Check(outgoing.Source is not null && outgoing.Visibility == Visibility.Visible,
                    "Show Today must capture the outgoing period before the command replaces its dates");
            model.TodayCommand.Execute(null);
            await Task.Delay(40);
            if (SystemParameters.ClientAreaAnimation)
                Check(Math.Sign(((TranslateTransform)content.RenderTransform).X) == -direction,
                    "Show Today must slide forward from past periods and backward from future periods");
            Check(!model.ShouldShowToday, "Animated Show Today must still select the current period");
            await Task.Delay(450);
            Check(outgoing.Source is null && Math.Abs(((TranslateTransform)content.RenderTransform).X) < .01,
                "Show Today must release its snapshot and restore the content after the carousel finishes");
        }
        model.View = CalendarView.ThreeDays;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
