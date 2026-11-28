using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wideska.Controls;
using Wideska.Infrastructure;
using Wideska.Models;
using Wideska.Providers;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flag);
    private static async Task RunResourceStressAsync(Application app)
    {
        string output = Path.GetFullPath("artifacts/resource-stress");
        Directory.CreateDirectory(output);
        string marker = Path.Combine(output, "phase.json");
        string stop = Path.Combine(output, "stop.flag");
        var measurements = new List<object>();
        using var process = Process.GetCurrentProcess();
        var session = Stopwatch.StartNew();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Wideska;component/Resources/Styles.xaml") });
        var provider = new ResourceProvider();
        using var model = new MainViewModel(new LocalStore(Path.Combine(output, "synthetic-store")), provider);
        await model.InitializeAsync();
        model.View = CalendarView.Week; model.TodayCommand.Execute(null);
        var window = new Wideska.MainWindow(model, false, enableIdleReturn: false) { Width = 720, Height = 660 };
        window.Show();
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        using var host = new DesktopHost(window);
        host.Attach(); host.SetBounds(new ScreenPlacement(40, 40, 720, 660));
        var timeline = (DayTimeline)window.FindName("Timeline");
        var outgoing = (Image)window.FindName("OutgoingCalendar");
        var incoming = (Image)window.FindName("IncomingCalendar");
        var next = (Button)window.FindName("NextPeriodButton");
        var previous = (Button)window.FindName("PreviousPeriodButton");
        int operations = 0;
        void Marker(string phase, bool completed = false) => File.WriteAllText(marker,
            JsonSerializer.Serialize(new { ProcessId = process.Id, Phase = phase, Completed = completed, Utc = DateTimeOffset.UtcNow }));
        void CheckStop() { if (File.Exists(stop)) throw new OperationCanceledException("Resource run stopped by the monitoring guard or operator."); }
        void Wheel(int delta) => timeline.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        void Navigate(int direction)
        {
            var button = direction > 0 ? next : previous;
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            (direction > 0 ? model.NextCommand : model.PreviousCommand).Execute(null);
        }
        async Task Phase(string name, int seconds, Func<int, Task>? action = null, int interval = 25)
        {
            Marker(name);
            Console.WriteLine($"RESOURCE PHASE: {name} ({seconds}s)");
            process.Refresh();
            double cpu = process.TotalProcessorTime.TotalSeconds;
            int layouts = timeline.LayoutPassCount, redraws = timeline.DrawPassCount;
            int operationStart = operations;
            var clock = Stopwatch.StartNew();
            int index = 0;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                CheckStop();
                if (action is not null) { await action(index++); operations++; }
                await Task.Delay(action is null ? 200 : interval);
            }
            process.Refresh();
            var memory = GC.GetGCMemoryInfo();
            measurements.Add(new
            {
                Phase = name, Seconds = clock.Elapsed.TotalSeconds, Operations = operations - operationStart,
                CpuPercentMachine = (process.TotalProcessorTime.TotalSeconds - cpu) / clock.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
                WorkingSetMiB = process.WorkingSet64 / 1048576d, PrivateMiB = process.PrivateMemorySize64 / 1048576d,
                ManagedMiB = GC.GetTotalMemory(false) / 1048576d, ManagedCommittedMiB = memory.TotalCommittedBytes / 1048576d,
                Handles = process.HandleCount, GdiObjects = GetGuiResources(process.Handle, 0), UserObjects = GetGuiResources(process.Handle, 1),
                Layouts = timeline.LayoutPassCount - layouts, Redraws = timeline.DrawPassCount - redraws,
                ScrollAnimating = timeline.IsScrollAnimating, SnapshotsAlive = outgoing.Source is not null || incoming.Source is not null
            });
        }
        var snapshotReferences = new List<WeakReference>();
        ImageSource? lastObservedSnapshot = null;
        try
        {
            await Phase("warmup", 10);
            await Phase("idle-small", 30);
            host.Detach(); window.Topmost = true;
            await Phase("wheel-small-visible", 30, i => { Wheel(i % 80 < 40 ? -120 : 120); return Task.CompletedTask; });
            await Phase("recover-wheel-visible", 20);
            Check(!timeline.IsScrollAnimating, "Wheel rendering must stop after stress");
            await Phase("arrows-small-visible", 30, i =>
            {
                Navigate(i % 6 < 3 ? 1 : -1);
                if (outgoing.Source is { } source && !ReferenceEquals(source, lastObservedSnapshot))
                { snapshotReferences.Add(new WeakReference(source)); lastObservedSnapshot = source; }
                return Task.CompletedTask;
            });
            await Phase("recover-arrows-visible", 20);
            Check(outgoing.Source is null && incoming.Source is null, "Carousel buffers must be released after stress");
            host.FitToDesktop();
            model.TodayCommand.Execute(null);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Phase("wheel-full-visible", 30, i => { Wheel(i % 80 < 40 ? -240 : 120); return Task.CompletedTask; });
            await Phase("arrows-full-visible", 30, i => { Navigate(i % 6 < 3 ? 1 : -1); return Task.CompletedTask; });
            await Phase("recover-full-visible", 15);
            window.Topmost = false; host.Attach();
            host.SetBounds(new ScreenPlacement(40, 40, 720, 660));
            model.View = CalendarView.Month;
            await Phase("month-navigation", 15, i => { Navigate(i % 20 < 10 ? 1 : -1); return Task.CompletedTask; }, 60);
            model.View = CalendarView.Year;
            await Phase("year-navigation", 15, i => { Navigate(i % 6 < 3 ? 1 : -1); return Task.CompletedTask; }, 100);
            model.View = CalendarView.Week; model.TodayCommand.Execute(null);
            host.SetBounds(new ScreenPlacement(40, 40, 720, 660));
            var details = new EventItem("Synthetic details", "10:00 - 10:30", "#38BDF8", "Synthetic details",
                Description: "Synthetic description", DescriptionMarkup: "<p><b>Synthetic description</b></p><ul><li>One</li><li>Two</li></ul>");
            await Phase("theme-and-details", 10, i =>
            {
                model.DarkTheme = i % 2 == 0;
                if (i % 2 == 0) model.OpenEventCommand.Execute(details);
                else model.CloseEventCommand.Execute(null);
                return Task.CompletedTask;
            }, 200);
            model.CloseEventCommand.Execute(null);
            window.Hide();
            await Phase("hidden", 25);
            Check(!timeline.IsScrollAnimating && outgoing.Source is null && incoming.Source is null,
                "Hidden widgets must not retain animation callbacks or carousel buffers");
            window.Show();
            for (int round = 0; round < 3; round++)
            {
                await Phase($"soak-cycle-{round + 1}", 15, i =>
                {
                    if (i % 4 == 0) Navigate(i % 40 < 20 ? 1 : -1);
                    else Wheel(i % 80 < 40 ? -120 : 120);
                    if (i % 35 == 0) { window.Hide(); window.Show(); }
                    return Task.CompletedTask;
                });
                await Phase($"soak-settle-{round + 1}", 5);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                await Phase($"post-gc-{round + 1}", 5);
            }
            model.DarkTheme = true; model.TodayCommand.Execute(null);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            timeline.ReturnToCurrentTime();
            await Task.Delay(2000);
            await Phase("final-idle", 40);
            lastObservedSnapshot = null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(!timeline.IsScrollAnimating && outgoing.Source is null && incoming.Source is null, "Stress must end without active rendering or snapshot buffers");
            Check(snapshotReferences.Count > 20 && snapshotReferences.Count(r => r.IsAlive) <= 2,
                "Old carousel snapshots must be collectible rather than accumulating with repeated navigation");
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                ProcessId = process.Id, DurationSeconds = session.Elapsed.TotalSeconds, Operations = operations,
                RenderTier = RenderCapability.Tier >> 16, Dpi = VisualTreeHelper.GetDpi(window).DpiScaleX,
                Screen = host.WorkArea(), SnapshotReferences = snapshotReferences.Count,
                LiveOldSnapshots = snapshotReferences.Count(r => r.IsAlive), Phases = measurements
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Marker("completed", true);
            host.Detach(); window.Close(); await model.ShutdownAsync();
        }
    }
    private sealed class ResourceProvider : ICalendarProvider
    {
        public Task<bool> IsConnectedAsync(CancellationToken token) => Task.FromResult(true);
        public Task ConnectAsync(string id, string secret, CancellationToken token) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken token) => Task.CompletedTask;
        public Task<CalendarSnapshot> GetSnapshotAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken token)
        {
            var events = new List<CalendarEvent>();
            for (DateTime day = start.LocalDateTime.Date; day < end.LocalDateTime.Date; day = day.AddDays(1))
            for (int item = 0; item < 16; item++)
            {
                var begin = new DateTimeOffset(day.AddHours(6 + item));
                events.Add(new($"synthetic-{day:yyyyMMdd}-{item}", "stress", $"Synthetic event {item + 1}", begin,
                    begin.AddMinutes(item % 3 == 0 ? 90 : 30), false, item % 2 == 0 ? "#38BDF8" : "#34D399", "", "<p><b>Test description</b></p><p>Synthetic calendar data only.</p>"));
            }
            return Task.FromResult(new CalendarSnapshot([new("stress", "Synthetic stress calendar", "#38BDF8")], events, DateTimeOffset.Now, start, end));
        }
        public void Dispose() { }
    }
}
