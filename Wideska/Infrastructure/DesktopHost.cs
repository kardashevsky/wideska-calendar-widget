using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wideska.Models;

namespace Wideska.Infrastructure;

// Uses the existing desktop icon host, not a bottom-ordered application window.
public sealed class DesktopHost : IDisposable
{
    private readonly Window window;
    private readonly IntPtr handle;
    private readonly HwndSource source;
    private readonly WinEventCallback foregroundCallback;
    private IntPtr foregroundHook;
    private bool disposed;
    public bool IsAttached { get; private set; }
    public IntPtr ParentHandle => GetParent(handle);
    public event Action? GeometryChanged;
    private const int Style = -16, ExtendedStyle = -20;
    private const long Child = 0x40000000, Popup = 0x80000000, ToolWindow = 0x80, NoActivate = 0x08000000, AppWindow = 0x40000;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    private delegate bool EnumCallback(IntPtr window, IntPtr data);
    private delegate void WinEventCallback(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetStyle64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetStyle64(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetStyle32(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetStyle32(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rectangle, IntPtr region, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    public DesktopHost(Window window)
    {
        this.window = window;
        handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle)!; source.AddHook(Hook);
        foregroundCallback = (_, _, _, _, _, _, _) =>
        {
            if (IsAttached && !window.Dispatcher.HasShutdownStarted)
                window.Dispatcher.BeginInvoke(() => { if (IsAttached) RaiseWithinDesktop(); });
        };
    }
    private static long ReadStyle(IntPtr window, int index) => IntPtr.Size == 8 ? GetStyle64(window, index).ToInt64() : (uint)GetStyle32(window, index);
    private static void WriteStyle(IntPtr window, int index, long value)
    {
        if (IntPtr.Size == 8) SetStyle64(window, index, new IntPtr(value)); else SetStyle32(window, index, unchecked((int)value));
    }
    public static IntPtr FindDesktopHost()
    {
        IntPtr desktop = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            IntPtr view = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return true;
            desktop = view; return false;
        }, IntPtr.Zero);
        return desktop;
    }
    public void Attach()
    {
        if (IsAttached) return;
        IntPtr parent = FindDesktopHost();
        if (parent == IntPtr.Zero) throw new InvalidOperationException("The Windows desktop is not available. Show Wideska again from the tray after Explorer starts.");
        var bounds = GetBounds();
        long original = ReadStyle(handle, Style);
        WriteStyle(handle, Style, (original | Child) & ~Popup);
        Marshal.SetLastPInvokeError(0);
        SetParent(handle, parent);
        if (GetParent(handle) != parent)
        {
            WriteStyle(handle, Style, original);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to attach Wideska to the Windows desktop.");
        }
        IsAttached = true;
        window.ShowActivated = false; window.ShowInTaskbar = false; window.Topmost = false;
        WriteStyle(handle, ExtendedStyle, (ReadStyle(handle, ExtendedStyle) | ToolWindow | NoActivate) & ~AppWindow);
        SetBounds(bounds);
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundCallback, 0, 0, 2); // foreground changes, skip this process
    }
    public void Detach()
    {
        if (!IsAttached) return;
        var bounds = GetBounds();
        if (foregroundHook != IntPtr.Zero) { UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
        IsAttached = false;
        SetParent(handle, IntPtr.Zero);
        WriteStyle(handle, Style, (ReadStyle(handle, Style) & ~Child) | Popup);
        WriteStyle(handle, ExtendedStyle, (ReadStyle(handle, ExtendedStyle) | ToolWindow) & ~(NoActivate | AppWindow));
        SetBounds(bounds);
    }
    public ScreenPlacement GetBounds()
    {
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
    public void SetBounds(ScreenPlacement bounds)
    {
        var point = new NativePoint { X = bounds.X, Y = bounds.Y };
        if (IsAttached) ScreenToClient(GetParent(handle), ref point);
        double scale = MonitorDpi() / 96d;
        SetWindowPos(handle, IntPtr.Zero, point.X, point.Y, Math.Max((int)Math.Ceiling(window.MinWidth * scale), bounds.Width), Math.Max((int)Math.Ceiling(window.MinHeight * scale), bounds.Height), 0x30); // frame changed, no activation
        ApplyMonitorScale();
        GeometryChanged?.Invoke();
    }
    private uint MonitorDpi() => GetDpiForMonitor(MonitorFromWindow(handle, 2), 0, out var x, out _) == 0 && x > 0 ? x : GetDpiForWindow(handle);
    private void ApplyMonitorScale()
    {
        if (window.Content is not FrameworkElement content) return;
        // Child HWNDs inherit the desktop's DPI; scale the WPF content for its actual monitor.
        double ratio = IsAttached ? MonitorDpi() / (VisualTreeHelper.GetDpi(window).DpiScaleX * 96d) : 1;
        if (content.LayoutTransform is ScaleTransform old && Math.Abs(old.ScaleX - ratio) < .001) return;
        content.LayoutTransform = new ScaleTransform(ratio, ratio);
    }
    public ScreenPlacement WorkArea()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
    }
    public void RecoverToScreen()
    {
        var bounds = GetBounds();
        var work = WorkArea();
        if (MonitorFromWindow(handle, 0) == IntPtr.Zero)
            SetBounds(bounds with { X = work.X, Y = work.Y });
    }
    public void FitToDesktop() => SetBounds(WorkArea());
    public void Resize(string edge, double dx, double dy)
    {
        var b = GetBounds(); double scale = MonitorDpi() / 96d;
        int x = (int)Math.Round(dx * scale), y = (int)Math.Round(dy * scale);
        int minW = (int)Math.Ceiling(window.MinWidth * scale), minH = (int)Math.Ceiling(window.MinHeight * scale);
        if (edge.Contains('E')) b = b with { Width = Math.Max(minW, b.Width + x) };
        if (edge.Contains('S')) b = b with { Height = Math.Max(minH, b.Height + y) };
        if (edge.Contains('W')) { int width = Math.Max(minW, b.Width - x); b = b with { X = b.X + b.Width - width, Width = width }; }
        if (edge.Contains('N')) { int height = Math.Max(minH, b.Height - y); b = b with { Y = b.Y + b.Height - height, Height = height }; }
        SetBounds(b);
    }
    public void RaiseWithinDesktop()
    {
        if (IsAttached) SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x13);
    }
    public void RefreshSurface()
    {
        if (!IsAttached) return;
        // Reparenting a layered WPF window can leave its first surface stale until a resize.
        if (window.Content is UIElement content) content.InvalidateVisual();
        var bounds = GetBounds();
        // Recreate the layered backing surface, then restore the exact requested geometry.
        SetBounds(bounds with { Width = bounds.Width + 1 });
        SetBounds(bounds);
        RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, 0x185); // invalidate, erase, all children, update now
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (IsAttached && message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE, preserve mouse input
        if (IsAttached && message is 0x7E or 0x2E0) { window.Dispatcher.BeginInvoke(() => { ApplyMonitorScale(); RecoverToScreen(); }); }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Detach(); }
        finally
        {
            if (foregroundHook != IntPtr.Zero) { UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
            IsAttached = false;
            source.RemoveHook(Hook);
        }
    }
}
