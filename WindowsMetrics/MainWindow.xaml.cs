using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsMetrics.Services;
using WindowsMetrics.ViewModels;

namespace WindowsMetrics;

public partial class MainWindow : Window
{
    private readonly AppSettings _appSettings;

    public MainWindow(MainViewModel vm, AppSettings appSettings)
    {
        InitializeComponent();
        _appSettings = appSettings;
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

    private const int WM_NCHITTEST = 0x0084;
    private const int HTRIGHT = 11;
    private const int HTLEFT = 10;
    private const int HTBOTTOMRIGHT = 17;
    private const int HTBOTTOMLEFT = 16;
    private const int ResizeGrip = 6; // px

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point pt);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            GetCursorPos(out var pt);
            var left   = Left;
            var right  = Left + ActualWidth;
            var bottom = Top + ActualHeight;
            bool nearRight  = pt.X >= right  - ResizeGrip;
            bool nearLeft   = pt.X <= left   + ResizeGrip;
            bool nearBottom = pt.Y >= bottom - ResizeGrip;

            if (nearRight && nearBottom) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
            if (nearLeft  && nearBottom) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
            if (nearRight)               { handled = true; return (IntPtr)HTRIGHT; }
            if (nearLeft)                { handled = true; return (IntPtr)HTLEFT; }
        }
        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);

        MicaHelper.Apply(this);
        var pos = WindowPositionService.Load();
        if (pos.HasValue)
        {
            Left = pos.Value.Left;
            Top = pos.Value.Top;
            Width = Math.Clamp(pos.Value.Width, 260, 800);
        }
        else
        {
            // Default: top-right
            var screen = System.Windows.SystemParameters.WorkArea;
            Left = screen.Right - Width - 16;
            Top = screen.Top + 16;
        }

        // Restore calendar collapsed state
        if (_appSettings.IsCalendarExpanded)
        {
            CalendarContent.Visibility = Visibility.Visible;
            CalendarToggle.Text = "▼";
            DtSection.StartTimer();
        }
        else
        {
            CalendarContent.Visibility = Visibility.Collapsed;
            CalendarToggle.Text = "▶";
            DtSection.StopTimer();
        }

        // Initialize font settings combo box
        _suppressFontEvents = true;
        var families = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(n => n)
            .ToList();
        FontFamilyCb.ItemsSource = families;
        FontFamilyCb.SelectedItem = _appSettings.FontFamilyName;

        var sizes = Enumerable.Range(8, 17).Select(i => (double)i).ToList();
        FontSizeCb.ItemsSource = sizes;
        FontSizeCb.SelectedItem = Math.Round(_appSettings.FontSize);
        _suppressFontEvents = false;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        WindowPositionService.Save(Left, Top, Width);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    public void ToggleSettings()
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void CalendarHeader_Toggle(object sender, MouseButtonEventArgs e)
    {
        bool expanding = CalendarContent.Visibility != Visibility.Visible;
        CalendarContent.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
        CalendarToggle.Text = expanding ? "▼" : "▶";
        if (expanding) DtSection.StartTimer(); else DtSection.StopTimer();
        _appSettings.IsCalendarExpanded = expanding;
        _appSettings.Save();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private bool _suppressFontEvents;

    private void FontFamilyCb_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressFontEvents || FontFamilyCb.SelectedItem is not string selected) return;
        Application.Current.Resources["AppFontFamily"] = new FontFamily(selected);
        _appSettings.FontFamilyName = selected;
        _appSettings.Save();
    }

    private void FontSizeCb_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressFontEvents || FontSizeCb.SelectedItem is not double selected) return;
        Application.Current.Resources["AppFontSize"] = selected;
        Application.Current.Resources["AppFontSizeSmall"] = Math.Max(6.0, selected - 2);
        _appSettings.FontSize = selected;
        _appSettings.Save();
    }
}
