using System.Windows;
using System.Windows.Controls;

namespace WindowsMetrics.Views.Sections;

public partial class MemorySection : UserControl
{
    public MemorySection()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateBars();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => UpdateBars();

    public void UpdateBars()
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        double w = ActualWidth - 20;
        if (w <= 0) return;
        MemBar.Width = w * vm.MemUsagePercent / 100;
        PageBar.Width = w * vm.PagefilePercent / 100;
    }
}
