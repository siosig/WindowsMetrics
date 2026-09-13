using WindowsMetrics.Models;
using WindowsMetrics.Services;

namespace WindowsMetrics.Tests;

public class MetricsParserTests
{
    private static List<FlatMetric> BuildRequest(string metricName, double value,
        Dictionary<string, string>? labels = null, bool isCumulative = false, ulong timeNano = 1_000_000_000)
    {
        return [new FlatMetric(metricName, labels ?? [], value, timeNano, isCumulative)];
    }

    [Fact]
    public void Parse_MemoryAvailable_SetCorrectly()
    {
        var parser = new MetricsParser();
        var flat = BuildRequest("windows_memory_available_bytes", 4_000_000_000);
        var snapshot = parser.Parse(flat);
        Assert.Equal(4_000_000_000, snapshot.Memory.AvailableBytes);
    }

    [Fact]
    public void Parse_ProcessCount_SetCorrectly()
    {
        var parser = new MetricsParser();
        var flat = BuildRequest("windows_system_processes", 360);
        var snapshot = parser.Parse(flat);
        Assert.Equal(360, snapshot.System.ProcessCount);
    }

    [Fact]
    public void Parse_CpuCounter_FirstCall_ReturnsZero()
    {
        var parser = new MetricsParser();
        var flat = BuildRequest("windows_cpu_time_total", 100,
            new Dictionary<string, string> { ["mode"] = "idle", ["core"] = "0" }, isCumulative: true);
        var snapshot = parser.Parse(flat);
        // First sample has no prior sample to diff against, so no rate can be computed yet.
        Assert.Null(snapshot.Cpu.IdlePercent);
    }

    [Fact]
    public void Parse_UnknownMetric_DoesNotThrow()
    {
        var parser = new MetricsParser();
        var flat = BuildRequest("unknown_metric_xyz", 42);
        var ex = Record.Exception(() => parser.Parse(flat));
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_DiskVolume_CreatesEntry()
    {
        var parser = new MetricsParser();
        var flat = BuildRequest("windows_logical_disk_percent_disk_time", 5.0,
            new Dictionary<string, string> { ["volume"] = "C:" });
        var snapshot = parser.Parse(flat);
        Assert.Single(snapshot.Disks);
        Assert.Equal("C:", snapshot.Disks[0].DriveLetter);
    }

    [Fact]
    public void Parse_EmptyBatch_PreservesLatestValues()
    {
        // A failed scrape passes an empty batch through so the connection-state update
        // still happens, but it must not wipe out previously observed metric values.
        var parser = new MetricsParser();
        parser.Parse(BuildRequest("windows_memory_available_bytes", 4_000_000_000));

        var snapshot = parser.Parse([]);

        Assert.Equal(4_000_000_000, snapshot.Memory.AvailableBytes);
    }
}
