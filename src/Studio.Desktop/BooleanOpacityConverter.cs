using System.Globalization;
using System.Windows.Data;

namespace Studio.Desktop;
public sealed class BooleanOpacityConverter : IValueConverter
{
    public static BooleanOpacityConverter Instance { get; } = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 1d : .4;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
