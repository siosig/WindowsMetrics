using WindowsMetrics.Models;
using WindowsMetrics.Services.Sensors;

namespace WindowsMetrics.Tests.Sensors;

public class HardwareSensorSelectorTests
{
    private static FlatMetric CpuTemp(string sensorName, double value, string hardwareId = "/intelcpu/0") =>
        new("lhm_cpu_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = sensorName, ["hardwareId"] = hardwareId }, value, 1, false);

    private static FlatMetric GpuTemp(string metric, string sensorName, double value, string hardwareId = "/gpu-nvidia/0") =>
        new(metric, new Dictionary<string, string> { ["sensorName"] = sensorName, ["hardwareId"] = hardwareId }, value, 1, false);

    private static FlatMetric BoardTemp(string sensorName, double value) =>
        new("lhm_motherboard_temperature_celsius", new Dictionary<string, string> { ["sensorName"] = sensorName }, value, 1, false);

    private static FlatMetric BoardFan(string sensorName, double value) =>
        new("lhm_motherboard_fan_rpm", new Dictionary<string, string> { ["sensorName"] = sensorName }, value, 1, false);

    [Theory]
    [InlineData(0, false)]
    [InlineData(0.1, true)]
    [InlineData(125, true)]
    [InlineData(125.01, false)]
    public void IsValidTemperature_ChecksBoundaries(double celsius, bool expected)
    {
        Assert.Equal(expected, HardwareSensorSelector.IsValidTemperature(celsius));
    }

    [Fact]
    public void IsValidTemperature_NaN_IsInvalid()
    {
        Assert.False(HardwareSensorSelector.IsValidTemperature(double.NaN));
    }

    [Fact]
    public void SelectCpuPackage_IntelCpuPackage_IsSelected()
    {
        var flat = new List<FlatMetric> { CpuTemp("CPU Package", 50.5) };
        Assert.Equal(50.5, HardwareSensorSelector.SelectCpuPackage(flat));
    }

    [Fact]
    public void SelectCpuPackage_TriesAmdNamesInPriorityOrder()
    {
        // Only "Core (Tdie)" and "Core (Tctl)" present (no "CPU Package", no "Core (Tctl/Tdie)"):
        // Tdie must win since it comes first among the names actually present.
        var flat = new List<FlatMetric> { CpuTemp("Core (Tctl)", 60), CpuTemp("Core (Tdie)", 55) };
        Assert.Equal(55, HardwareSensorSelector.SelectCpuPackage(flat));
    }

    [Fact]
    public void SelectCpuPackage_FallsBackToCpuCoresForAmd10h()
    {
        var flat = new List<FlatMetric> { CpuTemp("CPU Cores", 48) };
        Assert.Equal(48, HardwareSensorSelector.SelectCpuPackage(flat));
    }

    [Fact]
    public void SelectCpuPackage_MultipleCpusWithSameName_ReturnsMaximum()
    {
        var flat = new List<FlatMetric>
        {
            CpuTemp("CPU Package", 50, "/intelcpu/0"),
            CpuTemp("CPU Package", 55, "/intelcpu/1"),
        };
        Assert.Equal(55, HardwareSensorSelector.SelectCpuPackage(flat));
    }

    [Fact]
    public void SelectCpuPackage_TopPriorityNameOnlyInvalid_FallsThroughToNextName()
    {
        var flat = new List<FlatMetric> { CpuTemp("CPU Package", 0), CpuTemp("Core (Tctl/Tdie)", 52) };
        Assert.Equal(52, HardwareSensorSelector.SelectCpuPackage(flat));
    }

    [Fact]
    public void SelectCpuPackage_NoSeries_ReturnsNull()
    {
        Assert.Null(HardwareSensorSelector.SelectCpuPackage([]));
    }

    [Fact]
    public void SelectCpuPackage_NameMatchIsCaseAndWhitespaceInsensitive()
    {
        var flat = new List<FlatMetric> { CpuTemp("  cpu package  ", 51) };
        Assert.Equal(51, HardwareSensorSelector.SelectCpuPackage(flat));
    }

    [Fact]
    public void SelectGpuCore_TakesMaximumAcrossVendorSeries()
    {
        var flat = new List<FlatMetric>
        {
            GpuTemp("lhm_gpunvidia_temperature_celsius", "GPU Core", 43),
            GpuTemp("lhm_gpuamd_temperature_celsius", "GPU Core", 47, "/gpu-amd/0"),
        };
        Assert.Equal(47, HardwareSensorSelector.SelectGpuCore(flat));
    }

    [Fact]
    public void SelectGpuCore_IgnoresHotSpotSensor()
    {
        var flat = new List<FlatMetric>
        {
            GpuTemp("lhm_gpunvidia_temperature_celsius", "GPU Core", 43),
            GpuTemp("lhm_gpunvidia_temperature_celsius", "GPU Hot Spot", 90),
        };
        Assert.Equal(43, HardwareSensorSelector.SelectGpuCore(flat));
    }

    [Fact]
    public void SelectGpuCore_NoSeries_ReturnsNull()
    {
        Assert.Null(HardwareSensorSelector.SelectGpuCore([]));
    }

    [Fact]
    public void SelectBoard_DevMachineSensorNames_FillsAllSevenFields()
    {
        var flat = new List<FlatMetric>
        {
            BoardTemp("System", 47.4), BoardTemp("CPU", 48.9), BoardTemp("VRM MOS", 43),
            BoardTemp("PCH", 68), BoardTemp("CPU Socket", 43), BoardTemp("PCIe x1", 28.5),
            BoardTemp("M2 1", 31), BoardFan("CPU Fan", 0),
        };
        var result = HardwareSensorSelector.SelectBoard(flat);
        Assert.Equal(47.4, result.SysCelsius);
        Assert.Equal(48.9, result.BoardCpuCelsius);
        Assert.Equal(43, result.MosCelsius);
        Assert.Equal(68, result.PchCelsius);
        Assert.Equal(43, result.SockCelsius);
        Assert.Equal(28.5, result.PcieCelsius);
        Assert.Equal(31, result.M2Celsius);
        Assert.Equal(0, result.FanRpm);
    }

    [Theory]
    [InlineData("Motherboard", 40.0)]
    [InlineData("System #1", 41.0)]
    public void SelectBoard_SysFallbackNames_AreRecognized(string sensorName, double value)
    {
        var flat = new List<FlatMetric> { BoardTemp(sensorName, value) };
        Assert.Equal(value, HardwareSensorSelector.SelectBoard(flat).SysCelsius);
    }

    [Fact]
    public void SelectBoard_ChipsetFallsBackToPch()
    {
        var flat = new List<FlatMetric> { BoardTemp("Chipset", 65) };
        Assert.Equal(65, HardwareSensorSelector.SelectBoard(flat).PchCelsius);
    }

    [Fact]
    public void SelectBoard_VrmFallsBackToMos()
    {
        var flat = new List<FlatMetric> { BoardTemp("VRM", 44) };
        Assert.Equal(44, HardwareSensorSelector.SelectBoard(flat).MosCelsius);
    }

    [Fact]
    public void SelectBoard_M2WithDotNotation_IsRecognized()
    {
        var flat = new List<FlatMetric> { BoardTemp("M.2 1", 30) };
        Assert.Equal(30, HardwareSensorSelector.SelectBoard(flat).M2Celsius);
    }

    [Fact]
    public void SelectBoard_UnrecognizedSensorNames_AreNotAssignedToAnyField()
    {
        var flat = new List<FlatMetric> { BoardTemp("Temperature #1", 99), BoardTemp("Auxiliary", 88) };
        var result = HardwareSensorSelector.SelectBoard(flat);
        Assert.Null(result.SysCelsius);
        Assert.Null(result.BoardCpuCelsius);
        Assert.Null(result.MosCelsius);
        Assert.Null(result.PchCelsius);
        Assert.Null(result.SockCelsius);
        Assert.Null(result.PcieCelsius);
        Assert.Null(result.M2Celsius);
    }

    [Fact]
    public void SelectBoard_InvalidValueFallsThroughToNextName()
    {
        var flat = new List<FlatMetric> { BoardTemp("System", 0), BoardTemp("Motherboard", 41) };
        Assert.Equal(41, HardwareSensorSelector.SelectBoard(flat).SysCelsius);
    }

    [Fact]
    public void SelectBoard_ZeroRpmFan_IsValid()
    {
        var flat = new List<FlatMetric> { BoardFan("CPU Fan", 0) };
        Assert.Equal(0, HardwareSensorSelector.SelectBoard(flat).FanRpm);
    }

    [Fact]
    public void SelectBoard_FanTakesFirstInOutputOrder()
    {
        var flat = new List<FlatMetric> { BoardFan("CPU Fan", 0), BoardFan("Pump Fan", 2161.75) };
        Assert.Equal(0, HardwareSensorSelector.SelectBoard(flat).FanRpm);
    }

    [Fact]
    public void SelectBoard_NoSeries_AllFieldsNull()
    {
        var result = HardwareSensorSelector.SelectBoard([]);
        Assert.Null(result.SysCelsius);
        Assert.Null(result.FanRpm);
    }

    [Fact]
    public void SelectCpuCoreStats_PreferesPCoreEcoreNamesAndExcludesDerivedAndDistanceSensors()
    {
        var flat = new List<FlatMetric>
        {
            CpuTemp("CPU Package", 50.5),
            CpuTemp("Core Max", 51.5),
            CpuTemp("Core Average", 43.875),
            CpuTemp("P-Core 1", 49.25),
            CpuTemp("P-Core 2", 45.25),
            CpuTemp("E-Core 1", 42.75),
            CpuTemp("P-Core 1 Distance to TjMax", 50.75),
        };
        var (min, max, avg) = HardwareSensorSelector.SelectCpuCoreStats(flat);
        Assert.Equal(42.75, min);
        Assert.Equal(49.25, max);
        Assert.Equal((49.25 + 45.25 + 42.75) / 3, avg!.Value, precision: 6);
    }

    [Fact]
    public void SelectCpuCoreStats_NoCorePatternMatch_FallsBackToAllExceptDistanceToTjMax()
    {
        // Simulates a non-hybrid-Intel/AMD CPU: no "P-Core"/"E-Core" names present.
        var flat = new List<FlatMetric>
        {
            CpuTemp("CPU Package", 50),
            CpuTemp("Core (Tctl/Tdie)", 48),
            CpuTemp("Some Core Distance to TjMax", 90),
        };
        var (min, max, avg) = HardwareSensorSelector.SelectCpuCoreStats(flat);
        Assert.Equal(48, min);
        Assert.Equal(50, max);
        Assert.Equal(49, avg);
    }

    [Fact]
    public void SelectCpuCoreStats_NoSeries_ReturnsAllNull()
    {
        var (min, max, avg) = HardwareSensorSelector.SelectCpuCoreStats([]);
        Assert.Null(min);
        Assert.Null(max);
        Assert.Null(avg);
    }

    [Fact]
    public void SelectGpuStats_AggregatesAllReportedGpuTemperatureSensors()
    {
        var flat = new List<FlatMetric>
        {
            GpuTemp("lhm_gpunvidia_temperature_celsius", "GPU Core", 43),
            GpuTemp("lhm_gpunvidia_temperature_celsius", "GPU Hot Spot", 55.9),
        };
        var (min, max, avg) = HardwareSensorSelector.SelectGpuStats(flat);
        Assert.Equal(43, min);
        Assert.Equal(55.9, max);
        Assert.Equal((43 + 55.9) / 2, avg!.Value, precision: 6);
    }

    [Fact]
    public void SelectGpuStats_NoSeries_ReturnsAllNull()
    {
        var (min, max, avg) = HardwareSensorSelector.SelectGpuStats([]);
        Assert.Null(min);
        Assert.Null(max);
        Assert.Null(avg);
    }
}
