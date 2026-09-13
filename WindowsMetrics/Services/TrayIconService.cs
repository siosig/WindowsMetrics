using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace WindowsMetrics.Services;

public class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;

    public TrayIconService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        var icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;

        var menu = new ContextMenuStrip();
        menu.Items.Add("設定", null, (_, _) => OnSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => OnExit());

        _notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "DIOTLP Monitor",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.MouseDoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        });
    }

    private void OnSettings()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _mainWindow.Show();
            _mainWindow.Activate();
            _mainWindow.ToggleSettings();
        });
    }

    private void OnExit()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
