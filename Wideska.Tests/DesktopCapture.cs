using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Wideska.Models;
internal static partial class Program
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    private static void CaptureDesktopWidget(ScreenPlacement b)
    {
        IntPtr source = GetDC(IntPtr.Zero), target = CreateCompatibleDC(source), bitmap = CreateCompatibleBitmap(source, b.Width, b.Height);
        IntPtr old = SelectObject(target, bitmap);
        try
        {
            if (!BitBlt(target,0,0,b.Width,b.Height,source,b.X,b.Y,0x40CC0020)) throw new InvalidOperationException("Screen capture failed");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,System.Windows.Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = System.IO.File.Create("artifacts/desktop-widget-native.png"); encoder.Save(file);
        }
        finally { SelectObject(target,old); DeleteObject(bitmap); DeleteDC(target); ReleaseDC(IntPtr.Zero,source); }
    }
}
