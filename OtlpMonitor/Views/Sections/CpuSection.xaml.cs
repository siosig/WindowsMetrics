using System.Windows;
using System.Windows.Controls;

namespace OtlpMonitor.Views.Sections;

public partial class CpuSection : UserControl
{
    public CpuSection()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateSparkline();
        SizeChanged += (_, _) => UpdateSparkline();
    }

    public void UpdateSparkline()
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        double w = CpuCanvas.ActualWidth > 0 ? CpuCanvas.ActualWidth : 280;
        CpuLine.Points = vm.CpuSparkline.ToPoints(w, 24);
    }
}
