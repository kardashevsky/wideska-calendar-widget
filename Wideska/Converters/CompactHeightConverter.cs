using System.Globalization;
using System.Windows.Data;

namespace Wideska.Converters;

public sealed class CompactHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double height && height < double.Parse(parameter.ToString()!, CultureInfo.InvariantCulture);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
