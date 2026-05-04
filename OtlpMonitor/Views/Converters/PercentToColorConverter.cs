using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OtlpMonitor.Views.Converters;

[ValueConversion(typeof(double), typeof(Brush))]
public class PercentToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xFC, 0xE1, 0x00));
    private static readonly SolidColorBrush Critical = new(Color.FromRgb(0xFF, 0x43, 0x43));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double d) return GetAccentBrush();
        if (d >= 90) return Critical;
        if (d >= 80) return Warning;
        return GetAccentBrush();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    private static Brush GetAccentBrush()
    {
        if (System.Windows.Application.Current.Resources["SystemAccentColorLight2Brush"] is Brush b) return b;
        return new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF));
    }
}
