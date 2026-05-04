using System.Windows.Controls;

namespace OtlpMonitor.Views.Sections;

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
        NetLine.Points = vm.NetworkSparkline.ToPoints(w, 24);
    }
}
