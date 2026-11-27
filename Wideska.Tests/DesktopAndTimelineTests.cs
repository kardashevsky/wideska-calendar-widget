using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Wideska.Infrastructure;
using Wideska.Models;
using Wideska.Services;
using Wideska.ViewModels;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr WindowStyle(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, System.Text.StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr handle, uint relation);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr handle, uint attribute, out int value, int size);
    private static async Task TestDesktopHostAsync(Application app, Window window, DesktopIntegration desktop)
    {
        var handle = new WindowInteropHelper(window).Handle;
        Check(desktop.Host.IsAttached && desktop.Host.ParentHandle == DesktopHost.FindDesktopHost(), "Widget must be a real child of the Explorer desktop icon host");
        long style = WindowStyle(handle, -16).ToInt64(), extended = WindowStyle(handle, -20).ToInt64();
        Check((style & 0x40000000) != 0 && (style & 0x80000000) == 0, "Desktop window must use WS_CHILD, not WS_POPUP");
        Check((extended & 0x08000080) == 0x08000080 && (extended & 0x40000) == 0, "Desktop widget must be nonactivating and excluded from taskbar/Alt+Tab");
        var foreground = GetForegroundWindow(); desktop.Show();
        Check(GetForegroundWindow() == foreground, "Showing the desktop widget must not steal application focus");
        Check(SendMessage(handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt32() == 3, "Mouse activation must preserve clicks without activating the widget");
        var original = desktop.Host.GetBounds();
        desktop.Host.SetBounds(new ScreenPlacement(100, 100, 720, 560));
        desktop.Host.Resize("NW", 20, 10);
        var resized = desktop.Host.GetBounds();
        Check(resized.X > 100 && resized.Y > 100 && resized.Width < 720 && resized.Height < 560, "North-west resizing must move both origin and edges");
        desktop.Host.FitToDesktop();
        Check(desktop.Host.GetBounds() == desktop.Host.WorkArea(), "Fill must match the current monitor work area in physical pixels");
        if (Environment.GetCommandLineArgs().Contains("--desktop-smoke"))
        {
            Type shellType = Type.GetTypeFromProgID("Shell.Application")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var sentinel = new Window { Title = "Wideska desktop visibility probe", Width = 200, Height = 100, Left = 10, Top = 10, ShowInTaskbar = true };
            sentinel.Show();
            await Task.Delay(300);
            bool toggled = false;
            try
            {
                shell.MinimizeAll();
                await Task.Delay(1000);
                Check(IsIconic(new WindowInteropHelper(sentinel).Handle), "Desktop smoke test must actually minimize an ordinary application window");
                Check(IsWindowVisible(handle) && !IsIconic(handle) && IsWindowVisible(desktop.Host.ParentHandle), "Shell Show Desktop / Win+D must keep the hosted widget visible");
                desktop.Host.SetBounds(new ScreenPlacement(100,100,720,560));
                await Task.Delay(300);
                CaptureDesktopWidget(desktop.Host.GetBounds());
                shell.UndoMinimizeAll();
                sentinel.WindowState = WindowState.Normal; sentinel.Activate();
                await Task.Delay(300);
                shell.ToggleDesktop(); toggled = true;
                await Task.Delay(800);
                Console.WriteLine($"ToggleDesktop: ordinary minimized={IsIconic(new WindowInteropHelper(sentinel).Handle)}, widget visible={IsWindowVisible(handle)}, widget minimized={IsIconic(handle)}");
                Check(IsWindowVisible(handle) && !IsIconic(handle), "Win+D / ToggleDesktop must preserve the hosted widget in both toggle states");
            }
            finally { if (toggled) shell.ToggleDesktop(); shell.UndoMinimizeAll(); sentinel.Close(); Marshal.FinalReleaseComObject(shell); }
        }
        desktop.Host.SetBounds(original);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static async Task TestTimeIndicatorAsync(Application app, string directory)
    {
        Check(EventText.ToPlainText("<p>Review &amp; plan</p><ul><li>One</li><li>Two</li></ul>") == "Review & plan\n• One\n• Two", "Google HTML descriptions must become readable plain text");
        var instant = DateTimeOffset.Parse("2026-03-08T07:30:00Z");
        Check(CalendarTime.IndicatorMinutes(new DateTime(2026,3,8), instant, "America/New_York") == 210, "DST spring transition must use calendar-local 03:30");
        Check(CalendarTime.IndicatorMinutes(new DateTime(2026,3,7), instant, "America/New_York") is null, "Indicator must be absent from other dates");
        Check(CalendarTime.IndicatorMinutes(new DateTime(2026,3,8), instant, "Asia/Tokyo") == 990, "IANA time zone conversion must position the marker at 16:30");
        Check(CalendarTime.IndicatorMinutes(new DateTime(2026,11,1), DateTimeOffset.Parse("2026-11-01T06:30Z"), "America/New_York") == 90, "Repeated DST hour must map to the repeated wall-clock hour");
        var events = new[] { new EventItem("A", "", "#9876CD", "A", false, 60, 180), new EventItem("B", "", "#9876CD", "B", false, 90, 120), new EventItem("C", "", "#9876CD", "C", false, 240, 300) };
        var layout = TimelineLayout.Arrange(events);
        Check(layout[0].LaneCount == 2 && layout[1].Lane != layout[0].Lane && layout[2].LaneCount == 1, "Overlap groups must retain stable stacking offsets without narrowing unrelated events");
        Check(layout[0].HorizontalOffset + layout[0].WidthFraction > layout[1].HorizontalOffset && layout[2].WidthFraction == 1, "Concurrent cards must overlap horizontally while unrelated cards keep their full width");
        var day = new DayCell(new DateTime(2026,3,8), true, true, events);
        var timeline = new Wideska.Controls.DayTimeline { Days = new[] { day }, TimeZoneId = "America/New_York", CurrentTime = instant, Foreground = System.Windows.Media.Brushes.White, Width = 600, Height = 1600 };
        timeline.Measure(new Size(600,1600)); timeline.Arrange(new Rect(0,0,600,1600)); timeline.UpdateLayout();
        var image = new System.Windows.Media.Imaging.RenderTargetBitmap(600,1600,96,96,System.Windows.Media.PixelFormats.Pbgra32);
        image.Render(timeline);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using (var file = System.IO.File.Create("artifacts/timeline.png")) encoder.Save(file);
        double headerHeight = ((System.Windows.Media.DrawingVisual)System.Windows.Media.VisualTreeHelper.GetChild(timeline, 1)).ContentBounds.Height;
        double overlapY = headerHeight + 100d / 1440 * (timeline.ActualHeight - headerHeight - Wideska.Controls.DayTimeline.TimelineBottomPadding);
        Check(timeline.EventAt(new Point(200, overlapY))?.Title == "A" && timeline.EventAt(new Point(300, overlapY))?.Title == "B", "Exposed rear cards and topmost overlapping cards must retain the correct click targets");
        Check(timeline.ActualHeight == 1600, "Timeline must scale to its available drawing height");
        await TestCalendarClockAndPersistenceAsync(System.IO.Path.Combine(directory, "clock"));
    }
}
