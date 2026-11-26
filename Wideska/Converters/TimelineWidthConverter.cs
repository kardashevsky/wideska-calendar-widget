using System.Globalization;
using System.Windows.Data;
namespace Wideska.Converters;
public sealed class TimelineWidthConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is int columns ? Controls.DayTimeline.TimeGutterWidth + columns * 132 : Controls.DayTimeline.TimeGutterWidth + 132;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
