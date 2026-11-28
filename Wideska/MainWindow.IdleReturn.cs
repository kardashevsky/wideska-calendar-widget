using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wideska.Models;
using Wideska.Services;

namespace Wideska;

public partial class MainWindow
{
    internal static readonly TimeSpan IdleReturnDelay = TimeSpan.FromSeconds(10);
    private readonly DispatcherTimer idleReturnTimer = new(DispatcherPriority.Background) { Interval = IdleReturnDelay };
    private bool idleReturnEnabled;
    private bool idleReturnDisposed;
    private int idleActivityVersion;
    private Point? lastIdlePointerPosition;
    private bool CanReturnAfterIdle => !idleReturnDisposed && !quitting && IsVisible && model.ShowWidget && !model.SettingsOpen
        && !model.IsEventOpen && !CalendarViewSelector.IsDropDownOpen && Mouse.Captured is null
        && (!IsMouseOver || Mouse.LeftButton == MouseButtonState.Released);

    private void InitializeIdleReturn(bool enabled)
    {
        idleReturnEnabled = enabled;
        if (!enabled) return;
        idleReturnTimer.Tick += OnIdleReturnTick;
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWidgetInput), true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnWidgetInput), true);
        AddHandler(PreviewMouseUpEvent, new MouseButtonEventHandler(OnWidgetInput), true);
        AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnWidgetInput), true);
        PreviewMouseMove += OnIdlePointerMove;
    }
    private void OnWidgetInput(object sender, RoutedEventArgs e) => ResetIdleReturnTimer();
    private void OnIdlePointerMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(this);
        if (lastIdlePointerPosition is { } previous && (position - previous).LengthSquared < 1) return;
        lastIdlePointerPosition = position;
        ResetIdleReturnTimer();
    }
    internal void ResetIdleReturnTimer()
    {
        if (!idleReturnEnabled || idleReturnDisposed) return;
        lastIdlePointerPosition = Mouse.GetPosition(this);
        idleActivityVersion++;
        idleReturnTimer.Stop();
        Timeline.CancelIdleReturn();
        if (!quitting && IsVisible && model.ShowWidget && !model.SettingsOpen && !model.IsEventOpen) idleReturnTimer.Start();
    }
    private async void OnIdleReturnTick(object? sender, EventArgs e)
    {
        idleReturnTimer.Stop();
        int version = idleActivityVersion;
        try { await ReturnAfterIdleAsync(version); }
        catch (Exception ex) { model.ReportError(ex); }
        finally
        {
            if (version == idleActivityVersion && !idleReturnDisposed && !quitting && IsVisible && model.ShowWidget
                && !model.SettingsOpen && !model.IsEventOpen) idleReturnTimer.Start();
        }
    }
    internal Task ReturnAfterIdleAsync() => ReturnAfterIdleAsync(idleActivityVersion);
    private async Task ReturnAfterIdleAsync(int version)
    {
        if (!CanReturnAfterIdle || OutgoingCalendar.Source is not null) return;
        var today = CalendarTime.Local(model.CurrentTime, model.CalendarTimeZoneId).Date;
        bool wrongPeriod = model.View == CalendarView.Year ? model.NavigationDate.Year != today.Year
            : model.View == CalendarView.Month ? model.NavigationDate.Year != today.Year || model.NavigationDate.Month != today.Month
            : model.ShouldShowToday || !Timeline.IsTodayColumnVisible;
        if (wrongPeriod)
        {
            OnShowToday(TodayButton, new RoutedEventArgs());
            model.TodayCommand.Execute(null);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (OutgoingCalendar.Source is not null) await Task.Delay(350);
        }
        if (version != idleActivityVersion || !CanReturnAfterIdle || OutgoingCalendar.Source is not null) return;
        if (model.IsDays) Timeline.ReturnToCurrentTime();
    }
    private void DisposeIdleReturn()
    {
        idleReturnDisposed = true;
        idleActivityVersion++;
        idleReturnTimer.Stop();
        idleReturnTimer.Tick -= OnIdleReturnTick;
        Timeline.CancelIdleReturn();
        RemoveHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWidgetInput));
        RemoveHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnWidgetInput));
        RemoveHandler(PreviewMouseUpEvent, new MouseButtonEventHandler(OnWidgetInput));
        RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnWidgetInput));
        PreviewMouseMove -= OnIdlePointerMove;
    }
}
