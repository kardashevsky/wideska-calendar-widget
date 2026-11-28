using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Wideska.Models;
using Wideska.ViewModels;

internal static partial class Program
{
    private static async Task TestRapidNavigationAsync(Application app, Window window)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var model = (MainViewModel)window.DataContext;
        model.View = CalendarView.Week;
        model.TodayCommand.Execute(null);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        DateTime first = model.Days[0].Date;
        var next = (Button)window.FindName("NextPeriodButton");
        var previous = (Button)window.FindName("PreviousPeriodButton");
        var outgoing = (Image)window.FindName("OutgoingCalendar");
        var incoming = (Image)window.FindName("IncomingCalendar");
        void Click(Button button, System.Windows.Input.ICommand command)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            command.Execute(null);
        }
        Click(next, model.NextCommand);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var originalSnapshot = outgoing.Source;
        for (int i = 0; i < 24; i++) Click(next, model.NextCommand);
        for (int i = 0; i < 12; i++) Click(previous, model.PreviousCommand);
        Check(ReferenceEquals(outgoing.Source, originalSnapshot),
            "Repeated arrows must not recursively capture and resample the partially animated composition");
        Check(model.Days[0].Date == first.AddDays(13 * 7), "Every arrow click must update the requested week exactly once");
        await Task.Delay(850);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(outgoing.Source is null && incoming.Source is null && model.Days[0].Date == first.AddDays(13 * 7),
            "A burst must settle on its final requested week and release both snapshots");
        model.TodayCommand.Execute(null);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Click(next, model.NextCommand);
        originalSnapshot = outgoing.Source;
        for (int i = 0; i < 19; i++) Click(next, model.NextCommand);
        Check(ReferenceEquals(outgoing.Source, originalSnapshot), "Clicks before layout capture must reuse the same outgoing layer");
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(450);
        Check(outgoing.Source is null && incoming.Source is null && model.Days[0].Date == first.AddDays(20 * 7),
            "Clicks before snapshot preparation must collapse into one transition without a redundant follow-up");
        model.TodayCommand.Execute(null);
        model.View = CalendarView.ThreeDays;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
