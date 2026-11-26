using System.IO;
using System.Windows;
using System.Net;
using System.Net.Http;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wideska.Models;
using Wideska.Infrastructure;
using Wideska.Providers;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    private static int assertions;
    [STAThread]
    private static int Main()
    {
        int result = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunAsync(app); Console.WriteLine($"PASS: {assertions} assertions"); result = 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        });
        app.Run();
        return result;
    }
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static async Task RunAsync(Application app)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Wideska-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new LocalStore(directory);
            await store.WriteAsync("roundtrip.bin", new WidgetSettings { ClientId = "private-test-client" }, true);
            Check(!System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "roundtrip.bin"))).Contains("private-test-client"), "DPAPI must encrypt persisted data");
            Check((await store.ReadAsync<WidgetSettings>("roundtrip.bin", true))?.ClientId == "private-test-client", "Encrypted settings round trip");
            await File.WriteAllTextAsync(Path.Combine(directory, "invalid.bin"), "invalid");
            Check(await store.ReadAsync<WidgetSettings>("invalid.bin", false) is null, "Corrupt settings must recover");
            await TestGoogleProviderAsync(store);
            var fake = new FakeProvider();
            using var vm = new MainViewModel(store, fake);
            await vm.InitializeAsync();
            Check(vm.Connected && fake.Requests == 1, "Initial synchronization");
            Check(vm.Calendars.Count == 2, "Multiple calendars");
            fake.FailRequests = true;
            await vm.RefreshAsync();
            Check(vm.Status.Contains("cached") && vm.Calendars.Count == 2, "Offline errors must preserve cached events");
            fake.FailRequests = false;
            int requestsAfterOfflineCheck = fake.Requests;
            vm.View = CalendarView.Month;
            Check(vm.Days.Count == 42, "Month must contain six complete weeks");
            Check(vm.Days[0].Date.DayOfWeek == DayOfWeek.Monday, "Weeks start on Monday");
            vm.View = CalendarView.SingleDay;
            Check(vm.Days.Count == 1 && vm.Days[0].Date == DateTime.Today, "Single day view");
            Check(vm.Days[0].Events.Count == 3, "Timed, all-day and overnight events must overlap today");
            Check(vm.Days[0].Events[0].Time == "All day", "All-day events sort first");
            vm.Calendars[0].Visible = false;
            Check(vm.Days[0].Events.Count == 1, "Calendar visibility must filter events");
            vm.Calendars[0].Visible = true;
            vm.View = CalendarView.ThreeDays;
            Check(vm.Days.Count == 3 && vm.Days[2].Date == DateTime.Today.AddDays(2), "Three consecutive days");
            Check(vm.Days[1].Events.Count == 0, "All-day end dates must be exclusive");
            vm.View = CalendarView.Workweek;
            Check(vm.Days.Count == 5 && vm.Days[4].Date.DayOfWeek == DayOfWeek.Friday, "Workweek Monday to Friday");
            vm.View = CalendarView.Week;
            Check(vm.Days.Count == 7, "Full week");
            vm.NextCommand.Execute(null);
            Check(fake.Requests == requestsAfterOfflineCheck, "Navigation inside fresh cache must not request Google again");
            vm.TodayCommand.Execute(null);
            vm.View = CalendarView.Year;
            Check(vm.Months.Count == 12 && vm.Months.All(m => m.Days.Count == 42), "Year must contain twelve complete month grids");
            Check(vm.Quarters.Count == 4 && vm.Quarters.All(q => q.Months.Count == 3) && vm.Quarters.SelectMany(q => q.Months).SequenceEqual(vm.Months), "Year must group chronological months into four quarters for a two-by-six layout");
            Check(vm.Quarters.Select(q => q.Label).SequenceEqual(new[] { "Q1", "Q2", "Q3", "Q4" }), "Quarter labels must identify each group");
            vm.SelectDayCommand.Execute(new DateTime(2028, 2, 29));
            Check(vm.View == CalendarView.SingleDay && vm.Days[0].Date.Day == 29, "Leap-day navigation");
            vm.TodayCommand.Execute(null);
            await vm.RefreshAsync();
            // Load actual compiled XAML and render every view, including both themes and minimum dimensions.
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Wideska;component/Resources/Styles.xaml") });
            var window = new Wideska.MainWindow(vm, false) { Width = 960, Height = 680 };
            int bindingErrors = 0;
            var listener = new BindingListener(() => bindingErrors++);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            var activeBeforeStartup = GetForegroundWindow();
            window.Show();
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(GetForegroundWindow() == activeBeforeStartup, "Initial window presentation must not steal application focus while checking authorization");
            using (var desktop = new DesktopIntegration(window))
            {
                desktop.Apply(WindowBehavior.AlwaysOnTop);
                Check(!window.Topmost, "Legacy always-on-top settings must not raise the desktop widget above applications");
                desktop.Apply(WindowBehavior.Desktop);
                Check(!window.Topmost && !window.ShowInTaskbar, "Desktop window behavior");
                desktop.Apply(WindowBehavior.Normal);
                Check(!window.Topmost && !window.ShowInTaskbar, "A widget must remain absent from the taskbar in normal mode");
                await TestTrayAsync(app, window, desktop);
            }
            var selector = FindVisual<System.Windows.Controls.ComboBox>(window);
            Check(selector?.SelectedValue is CalendarView, "View selection must bind to the view enum");
            selector!.IsDropDownOpen = true;
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            selector.IsDropDownOpen = false;
            foreach (CalendarView view in Enum.GetValues<CalendarView>())
            {
                vm.View = view;
                await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.Measure(new Size(960, 680)); window.Arrange(new Rect(0, 0, 960, 680)); window.UpdateLayout();
                Check(window.ActualWidth == 960, $"Render {view}");
            }
            vm.View = CalendarView.Month;
            window.Width = 480; window.Height = 420;
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.Measure(new Size(480, 420)); window.Arrange(new Rect(0, 0, 480, 420)); window.UpdateLayout();
            Check(window.ActualWidth >= 480, "Minimum-size layout");
            window.Width = 960; window.Height = 680;
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.Measure(new Size(960, 680)); window.Arrange(new Rect(0, 0, 960, 680)); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 680, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string screenshot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "calendar-smoke.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            using (var output = File.Create(screenshot)) encoder.Save(output);
            Console.WriteLine("Rendered " + screenshot);
            vm.DarkTheme = false;
            vm.SettingsOpen = true;
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            Check(((SolidColorBrush)app.Resources["Surface"]).Color == (Color)ColorConverter.ConvertFromString("#FAF5F3FA"), "Light theme must update shared resources");
            bitmap = new RenderTargetBitmap(960, 680, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(Path.GetDirectoryName(screenshot)!, "settings-smoke.png"))) encoder.Save(output);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            Check(bindingErrors == 0, "XAML must have no binding errors");
            window.Close();
            await vm.ShutdownAsync();
            var saved = await store.ReadAsync<WidgetSettings>("settings.bin", true);
            Check(saved?.View == CalendarView.Month, "Settings must persist");
            await TestOnboardingAsync(app, directory);
            await TestTimeIndicatorAsync(app, directory);
        }
        finally { Directory.Delete(directory, true); }
    }
    private sealed class BindingListener(Action error) : System.Diagnostics.TraceListener
    {
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) { Console.Error.WriteLine(message); error(); } }
        public override void WriteLine(string? message) => Write(message);
    }
    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var found = FindVisual<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
    private sealed class FakeProvider : ICalendarProvider
    {
        public int Requests { get; private set; }
        public bool FailRequests { get; set; }
        public bool Connected { get; set; } = true;
        public bool FailAuthorization { get; set; }
        public bool SessionExpired { get; set; }
        public Task<bool> IsConnectedAsync(CancellationToken token) => Task.FromResult(Connected);
        public Task ConnectAsync(string id, string secret, CancellationToken token)
        {
            if (FailAuthorization) throw new CalendarAuthorizationException("Authorization declined");
            Connected = true; return Task.CompletedTask;
        }
        public Task DisconnectAsync(CancellationToken token) { Connected = false; return Task.CompletedTask; }
        public Task<CalendarSnapshot> GetSnapshotAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        {
            Requests++;
            if (SessionExpired) throw new CalendarAuthorizationException("Session expired");
            if (FailRequests) throw new HttpRequestException("Offline test");
            var today = new DateTimeOffset(DateTime.Today);
            return Task.FromResult(new CalendarSnapshot([new("work", "Work", "#92ACFF"), new("personal", "Personal", "#67D7B0")],
                [new("a", "work", "Planning session", today.AddHours(10), today.AddHours(11), false, "#92ACFF", "Office", "Weekly planning"),
                 new("b", "work", "Project day", today, today.AddDays(1), true, "#92ACFF", "", ""),
                 new("c", "personal", "Overnight event", today.AddHours(-2), today.AddHours(2), false, "#67D7B0", "", "")], DateTimeOffset.Now, start, end));
        }
        public void Dispose() { }
    }
}
