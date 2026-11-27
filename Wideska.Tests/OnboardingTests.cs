using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wideska.Models;
using Wideska.Providers;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    private static void SavePreview(Window window, string name, bool wallpaper = false)
    {
        window.UpdateLayout();
        int width = (int)window.ActualWidth;
        int height = (int)window.ActualHeight;
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        if (wallpaper)
        {
            // Synthetic wallpaper is used only to make glass transparency visible in test previews.
            var background = new DrawingVisual();
            using (var drawing = background.RenderOpen())
            {
                drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(29, 48, 74), Color.FromRgb(97, 73, 117), 35), null, new Rect(0, 0, width, height));
                drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(65, 174, 142, 170)), null, new Point(width * .8, height * .9), width * .7, height * .6);
            }
            image.Render(background);
        }
        image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        string path = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static async Task TestOnboardingAsync(Application app, string directory)
    {
        bool cancellationReported = false;
        var cancelled = new Wideska.Infrastructure.AsyncCommand(() => Task.FromException(new OperationCanceledException()), _ => cancellationReported = true);
        cancelled.Execute(null);
        Check(cancellationReported && cancelled.CanExecute(null), "Authorization timeouts must report cancellation and allow another attempt");
        var store = new LocalStore(Path.Combine(directory, "onboarding"));
        using (var expired = new MainViewModel(new LocalStore(Path.Combine(directory, "expired")), new FakeProvider { SessionExpired = true }))
        {
            await expired.InitializeAsync();
            Check(expired.ShowAuthorization && !expired.ShowWidget && expired.Calendars.Count == 0, "Expired stored consent must return to authorization before showing the calendar");
            await expired.ShutdownAsync();
        }
        await store.WriteAsync("calendar-cache.bin", new CalendarSnapshot([new("old", "Old account", "#123456")], [], DateTimeOffset.Now), true);
        var provider = new FakeProvider { Connected = false };
        using var vm = new MainViewModel(store, provider);
        Check(vm.ShowAuthorization && !vm.ShowWidget, "No calendar may flash before the initial account check");
        await vm.InitializeAsync();
        Check(vm.ShowAuthorization && !vm.ShowWidget && vm.Calendars.Count == 0 && provider.Requests == 0, "Signed-out launch must not display cached account data or request events");
        var window = new Wideska.MainWindow(vm, false) { Width = 480, Height = 740 };
        window.Show();
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(!((UIElement)window.FindName("WidgetRoot")).IsVisible && ((UIElement)window.FindName("AuthorizationRoot")).IsVisible, "Authorization must replace the calendar in the visual tree");
        SavePreview(window, "authorization-smoke");
        string config = Path.Combine(directory, "desktop-client.json");
        await File.WriteAllTextAsync(config, """{"web":{"client_id":"test.apps.googleusercontent.com"}}""");
        try { await vm.ImportCredentialsAsync(config); Check(false, "Web clients must be rejected"); }
        catch (InvalidOperationException) { Check(!vm.HasCredentials, "Rejected import must preserve credential configuration"); }
        await File.WriteAllTextAsync(config, """{"installed":{"client_id":"test.apps.googleusercontent.com","client_secret":"synthetic-secret"}}""");
        await vm.ImportCredentialsAsync(config);
        Check(vm.HasCredentials && vm.ClientSecret == "synthetic-secret", "Desktop JSON import must populate client configuration");
        provider.FailAuthorization = true;
        try { await vm.ConnectAsync(); Check(false, "Declined authorization must fail"); }
        catch (CalendarAuthorizationException) { Check(vm.ShowAuthorization && !vm.ShowWidget, "Declining consent must keep the authorization screen"); }
        provider.FailAuthorization = false;
        await vm.ConnectAsync();
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(vm.ShowWidget && !vm.ShowAuthorization && vm.Calendars.Count == 2, "Successful consent must reveal and synchronize the widget");
        vm.View = CalendarView.SingleDay; vm.TodayCommand.Execute(null);
        var selected = vm.Days[0].Events.First(e => e.HasDescription);
        vm.OpenEventCommand.Execute(selected);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(vm.IsEventOpen && !vm.IsCalendarEnabled && vm.SelectedEvent?.Description == "Weekly planning" && vm.SelectedEvent.Location == "Office", "Opening an event must expose structured details and block the calendar behind it");
        Check(((UIElement)window.FindName("EventDetailsOverlay")).IsVisible, "Event details must render as an in-widget modal overlay");
        SavePreview(window, "event-details", true);
        vm.CloseEventCommand.Execute(null);
        Check(!vm.IsEventOpen && vm.IsCalendarEnabled, "Closing details must restore calendar interaction");
        vm.FitDesktopCommand.Execute(null);
        Check(vm.IsDesktopFilled && !vm.CanMoveWidget && vm.WidgetCornerRadius.TopLeft == 0 && vm.CanEditGeometry, "Desktop fill must square corners and prevent dragging while permitting resize to exit");
        vm.FitDesktopCommand.Execute(null);
        Check(!vm.IsDesktopFilled && vm.CanMoveWidget && vm.WidgetCornerRadius.TopLeft == 24, "Restoring widget size must restore corners and movement");
        vm.FitDesktopCommand.Execute(null);
        ((System.Windows.Controls.Primitives.Thumb)window.FindName("ResizeGrip")).RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-40, -20) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        Check(!vm.IsDesktopFilled && vm.CanMoveWidget && vm.WidgetCornerRadius.TopLeft == 24, "Resizing out of desktop fill must immediately restore movable rounded mode");
        vm.View = CalendarView.Month;
        window.Width = 720; window.Height = 560;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(window.AllowsTransparency && window.Opacity == 1 && !window.ShowInTaskbar, "Layered widget must preserve text opacity and stay out of the taskbar");
        vm.BackgroundOpacity = 0;
        Check(((SolidColorBrush)app.Resources["WidgetGlass"]).Color.A == 0 && window.Opacity == 1, "Zero tint must make the background fully transparent without fading text");
        window.UpdateLayout();
        var alphaProbe = new RenderTargetBitmap(720, 560, 96, 96, PixelFormats.Pbgra32);
        alphaProbe.Render(window);
        var pixel = new byte[4];
        alphaProbe.CopyPixels(new Int32Rect(10, 280, 1, 1), pixel, 4, 0);
        Check(pixel[3] == 0, "Rendered widget background must have zero alpha at zero tint");
        vm.BackgroundOpacity = .14;
        Check(((SolidColorBrush)app.Resources["WidgetGlass"]).Color.A is > 0 and < 255, "Default glass must let the wallpaper show through");
        window.UpdateLayout();
        alphaProbe.Clear(); alphaProbe.Render(window);
        alphaProbe.CopyPixels(new Int32Rect(10, 280, 1, 1), pixel, 4, 0);
        Check(pixel[3] is > 0 and < 255, "Rendered default glass must actually contain translucent pixels");
        var grip = (System.Windows.Controls.Primitives.Thumb)window.FindName("ResizeGrip");
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(40, 20) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        Check(window.Width == 760 && window.Height == 580, "Corner grip must resize a transparent frameless window");
        vm.GeometryLocked = true;
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(40, 20) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        Check(window.Width == 760 && window.Height == 580 && !vm.CanEditGeometry, "Geometry lock must prevent resize gestures");
        vm.GeometryLocked = false;
        window.Width = 720; window.Height = 560;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SavePreview(window, "widget-transparent");
        SavePreview(window, "widget-wallpaper", true);
        vm.View = CalendarView.SingleDay;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SavePreview(window, "day-heading", true);
        vm.View = CalendarView.Month;
        window.Width = 420; window.Height = 360;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SavePreview(window, "widget-small", true);
        window.Width = 720; window.Height = 560;
        vm.View = CalendarView.Year;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SavePreview(window, "year-wallpaper", true);
        vm.View = CalendarView.Month;
        vm.DarkTheme = false;
        vm.SettingsOpen = true;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        SavePreview(window, "widget-settings", true);
        vm.DisconnectCommand.Execute(null);
        for (int i = 0; i < 200 && !vm.DisconnectCommand.CanExecute(null); i++) await Task.Delay(10);
        Check(vm.ShowAuthorization && !vm.ShowWidget && vm.Calendars.Count == 0, "Disconnect must immediately return to authorization");
        Check(!File.Exists(Path.Combine(directory, "onboarding", "calendar-cache.bin")), "Disconnect must delete cached event data");
        window.Close();
        await vm.ShutdownAsync();
    }
}
