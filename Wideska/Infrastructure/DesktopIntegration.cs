using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
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
    private bool revealing;
    private bool disposed;
    public event Action? QuitRequested;
    public event Action? SettingsRequested;
    public event Action? SyncRequested;
    public ContextMenu TrayMenu { get; }
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
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly uint showRequested = RegisterWindowMessage("Wideska.ShowWidget");
    public static void ShowRunningInstance() => PostMessage(new IntPtr(0xffff), RegisterWindowMessage("Wideska.ShowWidget"), IntPtr.Zero, IntPtr.Zero);
    public DesktopIntegration(Window window)
    {
        this.window = window;
        TrayMenu = new ContextMenu { Placement = PlacementMode.MousePoint };
        TrayMenu.SetResourceReference(FrameworkElement.StyleProperty, "TrayContextMenu");
        AddMenuItem("Show widget", Show);
        AddMenuItem("Hide widget (keep running)", window.Hide);
        TrayMenu.Items.Add(new Separator());
        AddMenuItem("Settings / Google sign-in", () => { Show(); SettingsRequested?.Invoke(); });
        AddMenuItem("Sync calendars", () => SyncRequested?.Invoke());
        AddMenuItem("Move to visible screen", () => { CenterOnScreen(); Show(); });
        TrayMenu.Items.Add(new Separator());
        AddMenuItem("Exit Wideska", () => QuitRequested?.Invoke());
        var handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle)!;
        source.AddHook(Hook);
        icon = new() { Size = Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = 1, Flags = 7, Callback = CallbackMessage, Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), Tip = "Wideska — desktop calendar", Info = "", Title = "" };
        if (!Shell_NotifyIcon(0, ref icon)) throw new InvalidOperationException("Unable to create the system tray icon.");
        window.Deactivated += OnDeactivated;
    }
    private void AddMenuItem(string title, Action action)
    {
        var item = new MenuItem { Header = title };
        item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItem");
        item.Click += (_, _) => { TrayMenu.IsOpen = false; action(); };
        TrayMenu.Items.Add(item);
    }
    public void Apply(WindowBehavior behavior)
    {
        desktop = behavior == WindowBehavior.Desktop;
        window.Topmost = behavior == WindowBehavior.AlwaysOnTop;
        window.ShowInTaskbar = false;
        SetWindowPos(icon.Window, new IntPtr(desktop && !revealing ? 1 : behavior == WindowBehavior.AlwaysOnTop ? -1 : -2), 0, 0, 0, 0, 0x13);
    }
    public void Show()
    {
        revealing = true;
        window.Show(); window.WindowState = WindowState.Normal;
        if (MonitorFromWindow(icon.Window, 0) == IntPtr.Zero) CenterOnScreen();
        SetWindowPos(icon.Window, window.Topmost ? new IntPtr(-1) : IntPtr.Zero, 0, 0, 0, 0, 0x13);
        window.Activate(); SetForegroundWindow(icon.Window);
    }
    public void CenterOnScreen()
    {
        window.WindowState = WindowState.Normal;
        var work = SystemParameters.WorkArea;
        window.Left = work.Left + Math.Max(0, (work.Width - window.Width) / 2);
        window.Top = work.Top + Math.Max(0, (work.Height - window.Height) / 2);
    }
    private void OnDeactivated(object? sender, EventArgs e)
    {
        revealing = false;
        if (desktop && !disposed) SetWindowPos(icon.Window, new IntPtr(1), 0, 0, 0, 0, 0x13);
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == showRequested) { Show(); handled = true; return IntPtr.Zero; }
        if ((uint)message == taskbarCreated) Shell_NotifyIcon(0, ref icon);
        if (desktop && !revealing && message == WindowPositionChanging)
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            position.After = new IntPtr(1); position.Flags &= ~0x4u;
            Marshal.StructureToPtr(position, lParam, false);
        }
        if (message != CallbackMessage) return IntPtr.Zero;
        if (lParam.ToInt32() is 0x0202 or 0x0203 or 0x0400 or 0x0401) { Show(); handled = true; }
        if (lParam.ToInt32() is 0x0205 or 0x007B)
        {
            SetForegroundWindow(hwnd);
            TrayMenu.IsOpen = true; handled = true;
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
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; TrayMenu.IsOpen = false;
        window.Deactivated -= OnDeactivated;
        Shell_NotifyIcon(2, ref icon); source.RemoveHook(Hook);
    }
}
