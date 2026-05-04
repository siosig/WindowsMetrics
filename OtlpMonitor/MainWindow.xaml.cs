using System.Windows;
using System.Windows.Input;
using OtlpMonitor.Services;
using OtlpMonitor.ViewModels;

namespace OtlpMonitor;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.SnapshotApplied += () =>
        {
            MemSection.UpdateBars();
            CpuSec.UpdateSparkline();
            NetSec.UpdateSparkline();
        };
        Loaded += OnLoaded;
        Closing += OnClosing;
        vm.StartConsuming(CancellationToken.None);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MicaHelper.Apply(this);
        var pos = WindowPositionService.Load();
        if (pos.HasValue)
        {
            Left = pos.Value.Left;
            Top = pos.Value.Top;
        }
        else
        {
            // デフォルト: 右上
            var screen = System.Windows.SystemParameters.WorkArea;
            Left = screen.Right - Width - 16;
            Top = screen.Top + 16;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        WindowPositionService.Save(Left, Top);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
