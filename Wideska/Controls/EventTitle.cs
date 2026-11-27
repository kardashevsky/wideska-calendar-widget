using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Wideska.Controls;

public sealed class EventTitle : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(EventTitle), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    internal static FormattedText Fit(string text, Typeface typeface, Brush brush, double width, double height, double preferredSize, double pixelsPerDip)
    {
        double minimumSize = Math.Min(preferredSize, 10);
        FormattedText Create(double size) => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, pixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), Trimming = TextTrimming.None };
        double selectedSize = preferredSize;
        var formatted = Create(selectedSize);
        for (double size = preferredSize - .5; formatted.Height > height && size >= minimumSize; size -= .5)
        {
            selectedSize = size;
            formatted = Create(size);
        }
        formatted.MaxTextHeight = Math.Max(typeface.FontFamily.LineSpacing * selectedSize, height);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        return formatted;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width) ? availableSize.Width : 300;
        double height = Math.Min(MaxHeight, double.IsFinite(availableSize.Height) ? availableSize.Height : 64);
        var text = Fit(Text, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), Foreground, width, height, FontSize, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return new Size(Math.Min(width, text.Width), Math.Min(height, text.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth < 1 || ActualHeight < 1) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawText(Fit(Text, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), Foreground, ActualWidth, ActualHeight, FontSize, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point());
        dc.Pop();
    }
}
