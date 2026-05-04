using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OtlpMonitor.Views.Converters;

[ValueConversion(typeof(double?), typeof(Brush))]
public class TempToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Normal = new(Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush Hot = new(Color.FromRgb(0xFF, 0x43, 0x43));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d && d >= 80) return Hot;
        return Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
