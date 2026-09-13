using OtlpMonitor.Models.Otlp;
using OtlpMonitor.Services;

namespace OtlpMonitor.Tests;

public class MetricsParserTests
{
    private static OtlpExportRequest BuildRequest(string metricName, double value,
        Dictionary<string, string>? labels = null, bool isSum = false, int temporality = 2)
    {
        var kvLabels = (labels ?? []).Select(kv => new KeyValue
        {
            Key = kv.Key,
            Value = new AnyValue { StringValue = kv.Value }
        }).ToList();

        var dp = new NumberDataPoint
        {
            Attributes = kvLabels,
            TimeUnixNano = "1000000000",
            AsDouble = value,
        };

        OtlpMetric metric;
        if (isSum)
        {
            metric = new OtlpMetric { Name = metricName, Sum = new SumData { DataPoints = [dp], AggregationTemporality = temporality, IsMonotonic = true } };
        }
        else
        {
            metric = new OtlpMetric { Name = metricName, Gauge = new GaugeData { DataPoints = [dp] } };
        }

        return new OtlpExportRequest
        {
            ResourceMetrics = [new ResourceMetrics
            {
                ScopeMetrics = [new ScopeMetrics { Metrics = [metric] }]
            }]
        };
    }

    [Fact]
    public void Parse_MemoryAvailable_SetCorrectly()
    {
        var parser = new MetricsParser();
        var req = BuildRequest("windows_memory_available_bytes", 4_000_000_000);
        var snapshot = parser.Parse(req);
        Assert.Equal(4_000_000_000, snapshot.Memory.AvailableBytes);
    }

    [Fact]
    public void Parse_ProcessCount_SetCorrectly()
    {
        var parser = new MetricsParser();
        var req = BuildRequest("windows_os_processes", 360);
        var snapshot = parser.Parse(req);
        Assert.Equal(360, snapshot.System.ProcessCount);
    }

    [Fact]
    public void Parse_CpuCounter_FirstCall_ReturnsZero()
    {
        var parser = new MetricsParser();
        var req = BuildRequest("windows_cpu_time_total", 100,
            new Dictionary<string, string> { ["mode"] = "idle", ["core"] = "0" }, isSum: true);
        var snapshot = parser.Parse(req);
        // 初回は差分計算不可 → IdlePercent は 0 になる
        Assert.Equal(0, snapshot.Cpu.IdlePercent);
    }

    [Fact]
    public void Parse_UnknownMetric_DoesNotThrow()
    {
        var parser = new MetricsParser();
        var req = BuildRequest("unknown_metric_xyz", 42);
        var ex = Record.Exception(() => parser.Parse(req));
        Assert.Null(ex);
    }

    [Fact]
    public void Parse_DiskVolume_CreatesEntry()
    {
        var parser = new MetricsParser();
        var req = BuildRequest("windows_logical_disk_percent_disk_time", 5.0,
            new Dictionary<string, string> { ["volume"] = "C:" });
        var snapshot = parser.Parse(req);
        Assert.Single(snapshot.Disks);
        Assert.Equal("C:", snapshot.Disks[0].DriveLetter);
    }
}
