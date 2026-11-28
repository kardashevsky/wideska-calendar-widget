using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wideska.Controls;

internal sealed class TaskDragAdorner : Adorner
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
    private readonly ImageSource image;
    private readonly Size size;
    private readonly Point grabOffset;
    private Point position;

    public TaskDragAdorner(UIElement surface, FrameworkElement card, Point grabOffset) : base(surface)
    {
        IsHitTestVisible = false;
        this.grabOffset = grabOffset;
        size = new(card.ActualWidth, card.ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(card);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(card);
        bitmap.Freeze();
        image = bitmap;
    }
    public void UpdatePosition()
    {
        if (!GetCursorPos(out var cursor)) return;
        var point = AdornedElement.PointFromScreen(new(cursor.X, cursor.Y));
        var next = new Point(point.X - grabOffset.X, point.Y - grabOffset.Y);
        if (next == position) return;
        position = next;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.PushOpacity(.94);
        drawingContext.DrawImage(image, new Rect(position, size));
        drawingContext.Pop();
    }
}
