using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Wideska.Converters;

public sealed class EventColorConverter : IValueConverter
{
    public static Color VibrantColor(string? value)
    {
        Color source;
        try { source = (Color)ColorConverter.ConvertFromString(value ?? "#38BDF8"); }
        catch (FormatException) { source = Color.FromRgb(56, 189, 248); }
        double maximum = Math.Max(source.R, Math.Max(source.G, source.B));
        double minimum = Math.Min(source.R, Math.Min(source.G, source.B));
        double range = maximum - minimum;
        if (range < 1) return Color.FromRgb(56, 189, 248);
        double brightness = Math.Max(205, maximum);
        double saturation = Math.Max(.65, range / maximum);
        byte Channel(byte channel) => (byte)Math.Round(brightness * (1 - saturation + saturation * (channel - minimum) / range));
        return Color.FromRgb(Channel(source.R), Channel(source.G), Channel(source.B));
    }

    public static Brush TextBrush(Color color)
    {
        static double Linear(byte channel) => channel / 255d <= .04045 ? channel / 255d / 12.92 : Math.Pow((channel / 255d + .055) / 1.055, 2.4);
        double luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        return luminance > .179 ? Brushes.Black : Brushes.White;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = VibrantColor(value as string);
        if (parameter as string == "Text") return TextBrush(color);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
