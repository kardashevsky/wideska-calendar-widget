using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using Wideska.Models;

namespace Wideska.Infrastructure;
public sealed class DesktopIntegration : IDisposable
{
    private readonly Window window;
    private readonly HwndSource source;
    private NotifyIconData icon;
    private const int CallbackMessage = 0x8001;
    private const int WindowPositionChanging = 0x0046;
    private bool desktop;
    public event Action? QuitRequested;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size; public IntPtr Window; public uint Id; public uint Flags; public uint Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition { public IntPtr Window; public IntPtr After; public int X; public int Y; public int Width; public int Height; public uint Flags; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    public DesktopIntegration(Window window)
    {
        this.window = window;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle)!;
        source.AddHook(Hook);
        icon = new() { Size = Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = 1, Flags = 7, Callback = CallbackMessage, Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), Tip = "Wideska — desktop calendar", Info = "", Title = "" };
        if (!Shell_NotifyIcon(0, ref icon)) throw new InvalidOperationException("Unable to create the system tray icon.");
    }
    public void Apply(WindowBehavior behavior)
    {
        desktop = behavior == WindowBehavior.Desktop;
        window.Topmost = behavior == WindowBehavior.AlwaysOnTop;
        window.ShowInTaskbar = !desktop;
        SetWindowPos(icon.Window, new IntPtr(desktop ? 1 : behavior == WindowBehavior.AlwaysOnTop ? -1 : -2), 0, 0, 0, 0, 0x13);
    }
    public void Show()
    {
        window.Show(); window.WindowState = WindowState.Normal;
        if (!desktop) { window.Activate(); SetForegroundWindow(icon.Window); }
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated) Shell_NotifyIcon(0, ref icon);
        if (desktop && message == WindowPositionChanging)
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            position.After = new IntPtr(1); position.Flags &= ~0x4u;
            Marshal.StructureToPtr(position, lParam, false);
        }
        if (message != CallbackMessage) return IntPtr.Zero;
        if (lParam.ToInt32() == 0x0203) { Show(); handled = true; }
        if (lParam.ToInt32() == 0x0205)
        {
            SetForegroundWindow(hwnd);
            var menu = new ContextMenu();
            var show = new MenuItem { Header = "Show Wideska" }; show.Click += (_, _) => Show();
            var quit = new MenuItem { Header = "Quit Wideska" }; quit.Click += (_, _) => QuitRequested?.Invoke();
            menu.Items.Add(show); menu.Items.Add(quit); menu.IsOpen = true; handled = true;
        }
        return IntPtr.Zero;
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the application path.");
            if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Start Wideska.exe directly to configure Windows startup.");
            key.SetValue("Wideska", "\"" + executable + "\"");
        }
        else key.DeleteValue("Wideska", false);
    }
    public void Dispose() { Shell_NotifyIcon(2, ref icon); source.RemoveHook(Hook); }
}
