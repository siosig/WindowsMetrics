using WindowsMetrics.Models;
using WindowsMetrics.Models.Display;
using WindowsMetrics.Models.Trends;

namespace WindowsMetrics.Tests.Trends;

public class MetricTrendsTests
{
    private static MetricsSnapshot WeSnapshot(bool succeeded, bool cpuKnown = true) => new()
    {
        Origin = new SnapshotOrigin(MetricsProvider.WindowsExporter, succeeded),
        Cpu = new CpuInfo { IdlePercent = cpuKnown ? 80 : null },
        Memory = new MemoryInfo { TotalBytes = 1000, AvailableBytes = 400 },
        Gpu = new GpuInfo { OverallPercent = 12.5, IsUsageKnown = true },
    };

    private static MetricsSnapshot LhmSnapshot(bool succeeded, double? mos = 43) => new()
    {
        Origin = new SnapshotOrigin(MetricsProvider.LibreHardwareMonitor, succeeded),
        Temperatures = new HardwareTemperatures
        {
            SysCelsius = 47, BoardCpuCelsius = 49, MosCelsius = mos, PchCelsius = 68,
            SockCelsius = 43, PcieCelsius = 28, M2Celsius = 31,
        },
    };

    [Fact]
    public void Record_WindowsExporterSuccessAllUsageKnown_AdvancesThreeUsageSeries()
    {
        var trends = new MetricTrends();
        var result = trends.Record(WeSnapshot(succeeded: true));

        Assert.Equal(TrendGroups.Usage, result);
        Assert.Equal(1, trends.CpuUsage.Count);
        Assert.Equal(1, trends.MemoryUsage.Count);
        Assert.Equal(1, trends.GpuUsage.Count);
    }

    [Fact]
    public void Record_Memory_PushesUsagePercentNotBytes()
    {
        // WeSnapshot's MemoryInfo has TotalBytes=1000, AvailableBytes=400, so UsagePercent=60
        // (a 0-100 value comparable on the same fixed axis as CPU/GPU usage).
        var trends = new MetricTrends();
        trends.Record(WeSnapshot(succeeded: true));

        Assert.Equal(60, trends.MemoryUsage.ToArray()[0]);
    }

    [Fact]
    public void Record_WindowsExporterSuccessCpuUnknown_SkipsCpuButAdvancesOthers()
    {
        var trends = new MetricTrends();
        var result = trends.Record(WeSnapshot(succeeded: true, cpuKnown: false));

        Assert.Equal(TrendGroups.Usage, result);
        Assert.Equal(0, trends.CpuUsage.Count);
        Assert.Equal(1, trends.MemoryUsage.Count);
        Assert.Equal(1, trends.GpuUsage.Count);
    }

    [Fact]
    public void Record_WindowsExporterFailure_AdvancesNothing()
    {
        var trends = new MetricTrends();
        var result = trends.Record(WeSnapshot(succeeded: false));

        Assert.Equal(TrendGroups.None, result);
        Assert.Equal(0, trends.CpuUsage.Count);
        Assert.Equal(0, trends.MemoryUsage.Count);
        Assert.Equal(0, trends.GpuUsage.Count);
    }

    [Fact]
    public void Record_LibreHardwareMonitorSuccessWithOneMissingSensor_AdvancesAllSevenWithAGap()
    {
        var trends = new MetricTrends();
        var result = trends.Record(LhmSnapshot(succeeded: true, mos: null));

        Assert.Equal(TrendGroups.BoardTemperature, result);
        Assert.Equal(1, trends.BoardSys.Count);
        Assert.Equal(1, trends.BoardMos.Count);
        Assert.True(double.IsNaN(trends.BoardMos.ToArray()[0]));
    }

    [Fact]
    public void Record_LibreHardwareMonitorFailure_AdvancesNothing()
    {
        var trends = new MetricTrends();
        var result = trends.Record(LhmSnapshot(succeeded: false));

        Assert.Equal(TrendGroups.None, result);
        Assert.Equal(0, trends.BoardSys.Count);
    }

    [Fact]
    public void Record_NoOrigin_AdvancesNothing()
    {
        var trends = new MetricTrends();
        var result = trends.Record(new MetricsSnapshot());

        Assert.Equal(TrendGroups.None, result);
    }
}
