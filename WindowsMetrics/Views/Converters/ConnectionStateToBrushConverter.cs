using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using WindowsMetrics.Models;

namespace WindowsMetrics.Views.Converters;

/// <summary>Maps <see cref="ConnectionState"/> to the title-bar indicator's fill color.</summary>
[ValueConversion(typeof(ConnectionState), typeof(Brush))]
public class ConnectionStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Online = new(Color.FromRgb(0x40, 0xC0, 0x60));
    private static readonly SolidColorBrush Degraded = new(Color.FromRgb(0xE0, 0xA0, 0x30));
    private static readonly SolidColorBrush Offline = new(Color.FromRgb(0xE0, 0x40, 0x40));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ConnectionState.AllOnline => Online,
        ConnectionState.Degraded => Degraded,
        _ => Offline,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
