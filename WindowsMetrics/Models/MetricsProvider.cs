namespace WindowsMetrics.Models;

/// <summary>
/// One of the two local metrics sources WindowsMetrics polls directly over HTTP.
/// </summary>
public enum MetricsProvider
{
    WindowsExporter,
    LibreHardwareMonitor,
}
