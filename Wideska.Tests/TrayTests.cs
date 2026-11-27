using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Wideska.Infrastructure;
using Wideska.Models;

internal static partial class Program
{
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    private static async Task TestTrayAsync(Application app, Window window, DesktopIntegration desktop)
    {
        bool settings = false, sync = false, exit = false;
        desktop.SettingsRequested += () => settings = true;
        desktop.SyncRequested += () => sync = true;
        desktop.QuitRequested += () => exit = true;
        var handle = new WindowInteropHelper(window).Handle;
        window.Hide();
        SendMessage(handle, 0x8001, IntPtr.Zero, new IntPtr(0x0202));
        Check(window.IsVisible, "Single tray click must restore the hidden widget");
        window.Hide();
        SendMessage(handle, 0x8001, IntPtr.Zero, new IntPtr(0x0205));
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(desktop.TrayMenu.IsOpen && desktop.TrayMenu.IsVisible, "Right tray click must open a persistent menu even when the widget is hidden");
        Check(desktop.TrayMenu.Items.OfType<MenuItem>().Count() == 8, "Tray must expose window actions plus geometry locking and desktop fill");
        var menuImage = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(desktop.TrayMenu.ActualWidth), (int)Math.Ceiling(desktop.TrayMenu.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        menuImage.Render(desktop.TrayMenu);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(menuImage));
        System.IO.Directory.CreateDirectory("artifacts");
        using (var file = System.IO.File.Create("artifacts/tray-menu.png")) encoder.Save(file);
        void Click(string label) => desktop.TrayMenu.Items.OfType<MenuItem>().Single(i => (string)i.Tag == label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Click("Show widget"); Check(window.IsVisible, "Show menu action");
        Click("Hide widget (keep running)"); Check(!window.IsVisible && !exit, "Hide must keep the process running");
        Click("Settings / Google sign-in"); Check(window.IsVisible && settings, "Settings must reveal the window and request settings or sign-in");
        Click("Sync calendars"); Check(sync, "Sync menu action");
        window.Left = -20000; window.Top = -20000;
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        desktop.Show();
        Check(window.Left >= SystemParameters.WorkArea.Left && window.Top >= SystemParameters.WorkArea.Top, "Show must recover a fully off-screen window");
        Click("Move to visible screen");
        Check(window.Left >= SystemParameters.WorkArea.Left && window.Top >= SystemParameters.WorkArea.Top, "Explicit position recovery");
        desktop.Apply(WindowBehavior.Desktop); window.Hide(); desktop.Show();
        Check(window.IsVisible && !window.Topmost, "Desktop mode must allow explicit reveal without becoming permanently topmost");
        await TestDesktopHostAsync(app, window, desktop);
        window.Hide(); SendMessage(handle, (int)RegisterWindowMessage("Wideska.ShowWidget"), IntPtr.Zero, IntPtr.Zero);
        Check(window.IsVisible, "A second launch must request showing the existing widget");
        desktop.Apply(WindowBehavior.Normal);
        Click("Exit Wideska"); Check(exit, "Exit must request complete application shutdown");
    }
}
