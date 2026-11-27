using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Wideska.Infrastructure;

internal static class ApplicationIcon
{
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    internal static IntPtr LoadTrayIcon(IntPtr window)
    {
        int size = GetSystemMetricsForDpi(49, GetDpiForWindow(window)); // SM_CXSMICON
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Wideska;component/Resources/Icons/Wideska.ico"));
        using var stream = resource.Stream;
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); reader.ReadUInt16();
        int count = reader.ReadUInt16();
        var entries = new List<(int Size, uint Length, uint Offset)>();
        for (int i = 0; i < count; i++)
        {
            int width = reader.ReadByte(); reader.ReadByte(); reader.ReadByte(); reader.ReadByte();
            reader.ReadUInt16(); reader.ReadUInt16();
            entries.Add((width == 0 ? 256 : width, reader.ReadUInt32(), reader.ReadUInt32()));
        }
        var selected = entries.OrderBy(e => e.Size < size ? 1 : 0).ThenBy(e => Math.Abs(e.Size - size)).First();
        stream.Position = selected.Offset;
        byte[] data = reader.ReadBytes(checked((int)selected.Length));
        IntPtr handle = CreateIconFromResourceEx(data, (uint)data.Length, true, 0x00030000, size, size, 0);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Unable to load the Wideska tray icon.");
        return handle;
    }
}
