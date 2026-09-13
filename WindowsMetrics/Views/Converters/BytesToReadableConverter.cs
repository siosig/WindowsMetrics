using System.Globalization;
using System.Windows.Data;

namespace OtlpMonitor.Views.Converters;

[ValueConversion(typeof(double), typeof(string))]
public class BytesToReadableConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d) return "---";
        if (d < 0) return "---";
        if (d >= 1_073_741_824) return $"{d / 1_073_741_824:0.00}G";
        if (d >= 1_048_576) return $"{d / 1_048_576:0.00}M";
        if (d >= 1024) return $"{d / 1024:0.00}k";
        return $"{d:0.00}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
