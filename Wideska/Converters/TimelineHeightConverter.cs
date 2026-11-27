using System.Globalization;
using System.Windows.Data;
namespace Wideska.Converters;
public sealed class TimelineHeightConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is double height && double.IsFinite(height) ? Math.Max(Controls.DayTimeline.MinimumTimelineHeight, height) : Controls.DayTimeline.MinimumTimelineHeight;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
