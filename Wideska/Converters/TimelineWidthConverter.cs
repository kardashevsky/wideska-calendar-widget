using System.Globalization;
using System.Windows.Data;
namespace Wideska.Converters;
public sealed class TimelineWidthConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is int columns ? 42d + columns * 110 : 152d;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
