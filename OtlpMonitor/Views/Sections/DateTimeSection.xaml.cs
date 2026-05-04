using System.Windows.Controls;
using System.Windows.Threading;

namespace OtlpMonitor.Views.Sections;

public partial class DateTimeSection : UserControl
{
    private readonly DispatcherTimer _timer;

    public DateTimeSection()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateTime();
        _timer.Start();
        UpdateTime();
    }

    private void UpdateTime()
    {
        var now = DateTime.Now;
        LocalTime.Text = now.ToString("yyyy-MM-dd  HH:mm:ss");
        UtcTime.Text = "UTC " + DateTime.UtcNow.ToString("HH:mm:ss");
    }
}
