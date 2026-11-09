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
    public void RequiredMetricPrefixes_DoesNotIncludeRemovedObjectsSeries()
    {
        // The Objects card (process/thread/handle counts, processor queue length) was removed;
        // its windows_exporter series must no longer be pulled out of the scrape at all.
        Assert.DoesNotContain(MetricsParser.RequiredMetricPrefixes, p => p.StartsWith("windows_system_"));
        Assert.DoesNotContain(MetricsParser.RequiredMetricPrefixes, p => p == "windows_process_handles");
    }

    [Fact]
    public void Parse_CpuCounter_FirstCall_ReturnsZero()
    {
        var parser = new MetricsParser();
        var flat = BuildRequest("windows_cpu_time_seconds_total", 100,
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

    private static List<FlatMetric> LhmTemperature(string metricName, string sensorName, double value, ulong timeNano = 1) =>
        [new FlatMetric(metricName, new Dictionary<string, string> { ["sensorName"] = sensorName }, value, timeNano, false)];

    [Fact]
    public void Parse_LhmCpuPackageArrivesAfterThermalZone_ReplacesThermalZoneValue()
    {
        var parser = new MetricsParser();
        var afterThermalZone = parser.Parse(BuildRequest("windows_thermalzone_temperature_celsius", 27.85));
        Assert.Equal(27.85, afterThermalZone.Cpu.TemperatureCelsius);

        var snapshot = parser.Parse(LhmTemperature("lhm_cpu_temperature_celsius", "CPU Package", 50.5));

        Assert.Equal(50.5, snapshot.Cpu.TemperatureCelsius);
    }

    [Fact]
    public void Parse_LhmCpuPackageTemperature_SurvivesSubsequentWindowsExporterCpuBatch()
    {
        var parser = new MetricsParser();
        parser.Parse(LhmTemperature("lhm_cpu_temperature_celsius", "CPU Package", 50.5));

        var weCpuBatch = BuildRequest("windows_cpu_time_seconds_total", 100,
            new Dictionary<string, string> { ["mode"] = "idle", ["core"] = "0" }, isCumulative: true);
        var snapshot = parser.Parse(weCpuBatch);

        Assert.Equal(50.5, snapshot.Cpu.TemperatureCelsius);
    }

    [Fact]
    public void Parse_LhmCpuPackageBecomesInvalid_FallsBackToThermalZone()
    {
        var parser = new MetricsParser();
        parser.Parse(BuildRequest("windows_thermalzone_temperature_celsius", 27.85));
        parser.Parse(LhmTemperature("lhm_cpu_temperature_celsius", "CPU Package", 50.5));

        // A reading of 0 is outside HardwareSensorSelector.IsValidTemperature's (0, 125] range.
        var snapshot = parser.Parse(LhmTemperature("lhm_cpu_temperature_celsius", "CPU Package", 0, timeNano: 2));

        Assert.Equal(27.85, snapshot.Cpu.TemperatureCelsius);
    }

    [Fact]
    public void Parse_GpuCoreTemperature_SurvivesSubsequentWindowsExporterBatch()
    {
        var parser = new MetricsParser();
        parser.Parse(LhmTemperature("lhm_gpunvidia_temperature_celsius", "GPU Core", 43));

        var snapshot = parser.Parse(BuildRequest("windows_memory_available_bytes", 4_000_000_000));

        Assert.Equal(43, snapshot.Gpu.TemperatureCelsius);
    }

    private static List<FlatMetric> BoardBatch() =>
    [
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "System" }, 47.4, 1, false),
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "CPU" }, 48.9, 1, false),
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "VRM MOS" }, 43, 1, false),
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "PCH" }, 68, 1, false),
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "CPU Socket" }, 43, 1, false),
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "PCIe x1" }, 28.5, 1, false),
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "M2 1" }, 31, 1, false),
        new("lhm_motherboard_fan_rpm", new Dictionary<string, string> { ["sensorName"] = "CPU Fan" }, 0, 1, false),
    ];

    [Fact]
    public void Parse_LhmMotherboardBatch_FillsAllSevenFieldsAndFan()
    {
        var parser = new MetricsParser();
        var snapshot = parser.Parse(BoardBatch());

        Assert.Equal(47.4, snapshot.Temperatures.SysCelsius);
        Assert.Equal(48.9, snapshot.Temperatures.BoardCpuCelsius);
        Assert.Equal(43, snapshot.Temperatures.MosCelsius);
        Assert.Equal(68, snapshot.Temperatures.PchCelsius);
        Assert.Equal(43, snapshot.Temperatures.SockCelsius);
        Assert.Equal(28.5, snapshot.Temperatures.PcieCelsius);
        Assert.Equal(31, snapshot.Temperatures.M2Celsius);
        Assert.Equal(0, snapshot.Temperatures.FanRpm);
    }

    [Fact]
    public void Parse_MotherboardTemperatures_SurviveSubsequentWindowsExporterBatch()
    {
        var parser = new MetricsParser();
        parser.Parse(BoardBatch());

        var snapshot = parser.Parse(BuildRequest("windows_memory_available_bytes", 4_000_000_000));

        Assert.Equal(47.4, snapshot.Temperatures.SysCelsius);
    }

    [Fact]
    public void Parse_MotherboardSysCelsius_IsNotDerivedFromThermalZone()
    {
        var parser = new MetricsParser();
        parser.Parse(BuildRequest("windows_thermalzone_temperature_celsius", 27.85));

        var snapshot = parser.Parse([]);

        Assert.Null(snapshot.Temperatures.SysCelsius);
    }

    [Fact]
    public void Parse_CpuCoreStats_SurviveSubsequentWindowsExporterBatch()
    {
        var parser = new MetricsParser();
        var lhmCpuBatch = new List<FlatMetric>
        {
            new("lhm_cpu_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "P-Core 1" }, 45, 1, false),
            new("lhm_cpu_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "P-Core 2" }, 49, 1, false),
        };
        parser.Parse(lhmCpuBatch);

        var snapshot = parser.Parse(BuildRequest("windows_memory_available_bytes", 4_000_000_000));

        Assert.Equal(45, snapshot.Cpu.MinTempC);
        Assert.Equal(49, snapshot.Cpu.MaxTempC);
        Assert.Equal(47, snapshot.Cpu.AvgTempC);
    }

    [Fact]
    public void Parse_GpuStats_SurviveSubsequentWindowsExporterBatch()
    {
        var parser = new MetricsParser();
        var lhmGpuBatch = new List<FlatMetric>
        {
            new("lhm_gpunvidia_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "GPU Core" }, 43, 1, false),
            new("lhm_gpunvidia_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = "GPU Hot Spot" }, 55.9, 1, false),
        };
        parser.Parse(lhmGpuBatch);

        var snapshot = parser.Parse(BuildRequest("windows_memory_available_bytes", 4_000_000_000));

        Assert.Equal(43, snapshot.Gpu.MinTempC);
        Assert.Equal(55.9, snapshot.Gpu.MaxTempC);
    }
}
