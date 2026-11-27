using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wideska.Controls;
using Wideska.Models;
using Wideska.Providers;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    private static async Task TestAuditRegressionsAsync(Application app, string directory)
    {
        string storage = Path.Combine(directory, "audit-store");
        await Task.WhenAll(Enumerable.Range(0, 16).Select(i => new LocalStore(storage).WriteAsync("parallel.bin", new WidgetSettings { Width = 500 + i }, true)));
        var store = new LocalStore(storage);
        Check((await store.ReadAsync<WidgetSettings>("parallel.bin", true))?.Width is >= 500 and < 516,
            "Concurrent store instances must leave one complete encrypted document");
        Check(Directory.GetFiles(storage, "*.tmp").Length == 0, "Atomic writes must clean up their unique temporary files");
        bool rejected = false;
        try { await store.WriteAsync("../escaped.bin", new WidgetSettings(), true); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Local storage must reject paths outside its directory");
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(storage, "parallel.bin"));
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await store.WriteAsync("parallel.bin", new WidgetSettings(), true, cancelled.Token); }
            catch (OperationCanceledException) { }
        }
        byte[] afterCancellation = await File.ReadAllBytesAsync(Path.Combine(storage, "parallel.bin"));
        Check(original.SequenceEqual(afterCancellation), "Cancelled writes must preserve the previous settings file");

        const string redirect = "http://127.0.0.1:12345/";
        Check(OAuthCallback.TryParse("GET /?state=expected&code=hello%2Bworld HTTP/1.1", redirect, "expected", out var values) && values["code"] == "hello+world",
            "Hardened OAuth callback parsing must preserve valid encoded authorization codes");
        Check(!OAuthCallback.TryParse("GET /?state=expected&state=other&code=x HTTP/1.1", redirect, "expected", out _)
            && !OAuthCallback.TryParse("GET /other?state=expected HTTP/1.1", redirect, "expected", out _)
            && !OAuthCallback.TryParse("GET https://example.com/?state=expected HTTP/1.1", redirect, "expected", out _),
            "OAuth callbacks must reject duplicate state parameters, wrong paths and absolute request targets");
        using (var oversized = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 20000))))
            Check(await OAuthCallback.ReadRequestLineAsync(oversized, CancellationToken.None) is null && oversized.Position == 8192,
                "Oversized loopback requests must be rejected after a bounded read");

        var watch = Stopwatch.StartNew();
        string malformed = string.Concat(Enumerable.Repeat("<a href='broken", 4000));
        _ = EventText.ToPlainText(malformed);
        Check(watch.Elapsed < TimeSpan.FromSeconds(5), "Malformed descriptions must not trigger catastrophic regex backtracking");
        Check(EventText.ToPlainText(new string('x', 200000)).Length <= 65537, "Oversized descriptions must have bounded rendering size");
        var links = new LinkTextBlock { ContentText = string.Join(" ", Enumerable.Range(0, 1000).Select(i => "https://example.com/" + i)) };
        Check(links.Inlines.OfType<Hyperlink>().Count() == 128, "Descriptions must not create unbounded hyperlink visual trees");
        var sleep = new EventItem("Sleep", "", "#000000", "", CalendarName: "Sleep");
        var sleepFill = Wideska.Converters.EventColorConverter.CardColor(sleep);
        var sleepText = ((System.Windows.Media.SolidColorBrush)Wideska.Converters.EventColorConverter.CardTextBrush(sleep)).Color;
        double alpha = sleepFill.A / 255d;
        var onWhite = System.Windows.Media.Color.FromRgb((byte)Math.Round(sleepFill.R * alpha + 255 * (1 - alpha)),
            (byte)Math.Round(sleepFill.G * alpha + 255 * (1 - alpha)), (byte)Math.Round(sleepFill.B * alpha + 255 * (1 - alpha)));
        static double Luminance(System.Windows.Media.Color color)
        {
            static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        Check(sleepFill.A < 255 && (Luminance(sleepText) + .05) / (Luminance(onWhite) + .05) >= 4.5,
            "Subdued Sleep cards must remain translucent while maintaining readable text on white wallpaper");
        Check(Wideska.Converters.EventColorConverter.VibrantColor("not-a-color").A == 255,
            "Malformed external colors must fall back to a valid opaque color");

        var provider = new FakeProvider();
        using (var vm = new MainViewModel(new LocalStore(Path.Combine(directory, "audit-vm")), provider))
        {
            await vm.InitializeAsync();
            int initializedRequests = provider.Requests;
            await vm.InitializeAsync();
            Check(provider.Requests == initializedRequests, "Repeated initialization must not start another synchronization loop");
            var firstDay = vm.Days.First(); var calendar = vm.Calendars.First();
            string cachePath = Path.Combine(directory, "audit-vm", "calendar-cache.bin");
            DateTime cacheWrite = File.GetLastWriteTimeUtc(cachePath);
            int collectionChanges = 0;
            vm.Days.CollectionChanged += (_, _) => collectionChanges++;
            for (int i = 0; i < 20; i++) await vm.RefreshAsync();
            Check(collectionChanges == 0 && ReferenceEquals(firstDay, vm.Days.First()) && ReferenceEquals(calendar, vm.Calendars.First()),
                "Unchanged synchronization must retain calendar items and avoid all collection rebuilds");
            Check(File.GetLastWriteTimeUtc(cachePath) == cacheWrite, "Unchanged synchronization must not rewrite the encrypted cache");
            provider.TimeoutRequests = true;
            await vm.RefreshAsync();
            Check(vm.Connected && vm.Status == AppLocalization.Text("Calendar request timed out. Try again shortly."), "Network timeouts must preserve the session and not be reported as authorization failures");
            provider.TimeoutRequests = false;
            vm.OpenEventCommand.Execute(vm.Days.SelectMany(d => d.Events).First(e => e.EventId == "a"));
            provider.PlanningTitleSuffix = " updated";
            await vm.RefreshAsync();
            Check(vm.SelectedEvent?.Title == "Planning session updated", "Open event details must update when the remote event changes");
            provider.PlanningEventId = "replacement";
            await vm.RefreshAsync();
            Check(!vm.IsEventOpen, "Deleting a selected event must close stale event details");
            var references = Enumerable.Range(0, 3).Select(_ => CreateClosedWindow(vm)).Append(CreateClosedWindow(vm, false)).ToArray();
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(300);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(references.All(reference => !reference.IsAlive), "Closed windows must not be retained by a live view model's event subscriptions");

            var window = new Wideska.MainWindow(vm, false) { Width = 3000, Height = 1800 };
            window.Show();
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            vm.View = CalendarView.Week;
            vm.UpdateCurrentTime(new DateTimeOffset(2026, 11, 29, 12, 0, 0, TimeSpan.Zero));
            vm.View = CalendarView.Workweek;
            vm.TodayCommand.Execute(null);
            Check(vm.View == CalendarView.SingleDay && vm.Days.Single().Today,
                "Show today must open a visible day when the current workweek excludes a weekend");
            vm.View = CalendarView.Week;
            window.Width = 420;
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var todayButton = (Button)window.FindName("TodayButton");
            var timeline = (DayTimeline)window.FindName("Timeline");
            Check(!timeline.IsTodayColumnVisible && todayButton.Visibility == Visibility.Visible,
                "Show today must remain available when today's column is outside the horizontal viewport");
            todayButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, todayButton));
            if (SystemParameters.ClientAreaAnimation)
                Check(((Image)window.FindName("OutgoingCalendar")).Source is not null,
                    "Revealing a horizontally hidden today column must use the same carousel transition");
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(timeline.IsTodayColumnVisible && todayButton.Visibility == Visibility.Collapsed,
                "Show today must reveal the current column before hiding itself");
            vm.UpdateCurrentTime(DateTimeOffset.UtcNow);
            window.Width = 3000;
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            vm.SettingsOpen = true;
            window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Escape)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
            Check(!vm.SettingsOpen, "Escape must dismiss settings without closing or hiding the widget");
            if (SystemParameters.ClientAreaAnimation)
            {
                var next = (Button)window.FindName("NextPeriodButton");
                next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, next));
                var outgoing = (Image)window.FindName("OutgoingCalendar");
                var snapshot = (BitmapSource)outgoing.Source;
                Check(Math.Max(snapshot.PixelWidth, snapshot.PixelHeight) <= 2048, "Large-screen navigation snapshots must be capped at 2048 pixels per side");
                window.Hide();
                Check(outgoing.Source is null, "Hiding the widget mid-transition must immediately release its snapshot");
            }
            window.Close();

            provider.DelayMilliseconds = 2000;
            Task refresh = vm.RefreshAsync();
            Task shutdown = vm.ShutdownAsync();
            Check(ReferenceEquals(shutdown, vm.ShutdownAsync()), "Repeated shutdown requests must share one completion task");
            await Task.WhenAll(refresh, shutdown);
            vm.Dispose(); vm.Dispose();
            await vm.RefreshAsync();
            Check(provider.Disposed, "Disposal must cancel active requests and release the calendar provider without later refresh errors");
        }
        using (var missingRange = new MainViewModel(new LocalStore(Path.Combine(directory, "audit-missing-range")), new FakeProvider { MissingRange = true }))
        {
            await missingRange.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await missingRange.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(missingRange.Connected, "Missing provider range metadata must not cause recursive request storms");
            await missingRange.ShutdownAsync();
        }
        string oversizedConfiguration = Path.Combine(directory, "oversized-oauth.json");
        await File.WriteAllTextAsync(oversizedConfiguration, new string(' ', 1024 * 1024 + 1));
        bool oversizedRejected = false;
        try { await GoogleOAuthConfiguration.ReadAsync(oversizedConfiguration); }
        catch (InvalidOperationException) { oversizedRejected = true; }
        Check(oversizedRejected, "OAuth imports must reject oversized local files before parsing them");
        using (var longEventVm = new MainViewModel(new LocalStore(Path.Combine(directory, "audit-long-event")),
            new FakeProvider { ProjectDuration = 40, ProjectDescription = "<p>" + new string('x', 10000) + "</p>" }))
        {
            await longEventVm.InitializeAsync();
            var copies = longEventVm.Days.SelectMany(d => d.Events).Where(e => e.EventId == "b").ToArray();
            Check(copies.Length > 1 && copies.All(e => ReferenceEquals(e.Description, copies[0].Description)),
                "Multi-day event cells must share one normalized description instead of duplicating large strings");
            Check(copies[0].Description.Length == 10000 && copies.All(e => e.Details.Length < 1000),
                "Hover previews must be bounded without losing the full modal description");
            await longEventVm.ShutdownAsync();
        }
        var disconnectProvider = new FakeProvider();
        string disconnectDirectory = Path.Combine(directory, "audit-disconnect");
        using (var disconnectVm = new MainViewModel(new LocalStore(disconnectDirectory), disconnectProvider))
        {
            await disconnectVm.InitializeAsync();
            string cache = Path.Combine(disconnectDirectory, "calendar-cache.bin");
            var attributes = File.GetAttributes(cache);
            File.SetAttributes(cache, attributes | FileAttributes.ReadOnly);
            try
            {
                disconnectVm.DisconnectCommand.Execute(null);
                for (int i = 0; i < 100 && !disconnectVm.DisconnectCommand.CanExecute(null); i++) await Task.Delay(10);
                Check(!await disconnectProvider.IsConnectedAsync(CancellationToken.None) && !disconnectVm.Connected,
                    "Cache deletion failures must not prevent Google account disconnection");
            }
            finally { if (File.Exists(cache)) File.SetAttributes(cache, attributes); }
            await disconnectVm.ShutdownAsync();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateClosedWindow(MainViewModel vm, bool show = true)
    {
        var window = new Wideska.MainWindow(vm, false);
        if (show) window.Show();
        window.Close();
        return new WeakReference(window);
    }
}
