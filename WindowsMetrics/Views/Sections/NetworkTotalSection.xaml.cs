using System.Windows.Controls;

namespace WindowsMetrics.Views.Sections;

public partial class NetworkTotalSection : UserControl
{
    public NetworkTotalSection()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateSparkline();
        SizeChanged += (_, _) => UpdateSparkline();
    }

    public void UpdateSparkline()
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        double w = NetCanvas.ActualWidth > 0 ? NetCanvas.ActualWidth : 280;
        const double h = 30;
        NetZeroLine.X2 = w;
        var (rx, tx) = vm.NetworkSparkline.ToPoints(w, h);
        NetRxLine.Points = rx;
        NetTxLine.Points = tx;
    }
}
