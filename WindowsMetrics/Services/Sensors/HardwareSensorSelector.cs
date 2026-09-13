using System.Text.RegularExpressions;
using WindowsMetrics.Models;
using WindowsMetrics.Models.Display;

namespace WindowsMetrics.Services.Sensors;

/// <summary>
/// Pure functions that pick temperature readings (and, for the Mother card, a fan speed) out of
/// a LibreHardwareMonitor (LHM) scrape batch, by trying short lists of known sensor names in
/// priority order. Stateless and UI-independent so the selection rules can be unit tested
/// directly against specs/005-ui-section-restructure/contracts/hardware-temperature-metrics.md.
/// </summary>
public static partial class HardwareSensorSelector
{
    /// <summary>
    /// A reading is trusted only in (0, 125] Celsius (research.md R-02): the upper bound matches
    /// the range LHM's own Nuvoton/Winbond Super I/O drivers already enforce before a sample is
    /// even emitted, and the lower bound rejects the 0 °C an unpopulated sensor tends to report.
    /// </summary>
    public static bool IsValidTemperature(double celsius) => celsius is > 0 and <= 125;

    private static readonly string[] CpuPackagePriority =
    [
        "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "CPU Cores",
    ];

    private static readonly string[] GpuTemperatureMetricNames =
    [
        "lhm_gpunvidia_temperature_celsius", "lhm_gpuamd_temperature_celsius", "lhm_gpuintel_temperature_celsius",
    ];

    private static readonly string[] SysNames = ["System", "Motherboard", "System #1"];
    private static readonly string[] BoardCpuNames = ["CPU", "CPU Core", "CPU (PECI)", "CPU Package"];
    private static readonly string[] MosNames = ["VRM MOS", "MOS", "VRM", "VSoC MOS"];
    private static readonly string[] PchNames = ["PCH", "Chipset", "PCH Chip"];
    private static readonly string[] SockNames = ["CPU Socket"];
    private static readonly string[] PcieNames = ["PCIe x1", "PCIe x16", "PCIe x4"];

    // Matches Intel's 12th-gen-and-later hybrid naming exactly ("P-Core 1", "E-Core 8", ...),
    // deliberately excluding the derived "Core Max"/"Core Average" sensors and the
    // non-temperature "... Distance to TjMax" sensors LHM reports under the same metric name
    // (research.md F-12).
    [GeneratedRegex(@"^[PE]-Core\s+\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex CpuCorePattern();

    /// <summary>
    /// Picks the CPU package temperature from <c>lhm_cpu_temperature_celsius</c>, trying vendor
    /// sensor names in priority order (Intel's "CPU Package" first, then AMD Zen's Tctl/Tdie
    /// variants, then AMD 10h's "CPU Cores"). Returns null if the batch has no matching series,
    /// or every candidate name only ever reported an out-of-range value.
    /// </summary>
    public static double? SelectCpuPackage(IReadOnlyList<FlatMetric> flat)
    {
        var samples = flat.Where(f => f.Name == "lhm_cpu_temperature_celsius").ToList();
        return SelectByNamePriority(samples, CpuPackagePriority);
    }

    /// <summary>
    /// Picks the GPU core temperature: the maximum valid "GPU Core" reading across all three
    /// vendor-specific series (NVIDIA/AMD/Intel), covering the case of more than one GPU.
    /// </summary>
    public static double? SelectGpuCore(IReadOnlyList<FlatMetric> flat)
    {
        var values = flat
            .Where(f => GpuTemperatureMetricNames.Contains(f.Name) && SensorNameMatches(f, "GPU Core"))
            .Select(f => f.Value)
            .Where(IsValidTemperature)
            .ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    /// <summary>
    /// Fills every field of <see cref="HardwareTemperatures"/> (except the trend-derived
    /// min/max/avg) from a LHM motherboard batch, matching each Mother-row item against a short
    /// alias list (contracts/hardware-temperature-metrics.md "Mother の各項目"). A field an alias
    /// list has no match for is left null (rendered as "--"), never guessed from an unrelated
    /// sensor name such as "Temperature #1" or "Auxiliary".
    /// </summary>
    public static HardwareTemperatures SelectBoard(IReadOnlyList<FlatMetric> flat)
    {
        var samples = flat.Where(f => f.Name == "lhm_motherboard_temperature_celsius").ToList();
        return new HardwareTemperatures
        {
            SysCelsius = SelectByNamePriority(samples, SysNames),
            BoardCpuCelsius = SelectByNamePriority(samples, BoardCpuNames),
            MosCelsius = SelectByNamePriority(samples, MosNames),
            PchCelsius = SelectByNamePriority(samples, PchNames),
            SockCelsius = SelectByNamePriority(samples, SockNames),
            PcieCelsius = SelectByNamePriority(samples, PcieNames),
            M2Celsius = SelectByPrefix(samples, "M2") ?? SelectByPrefix(samples, "M.2"),
            FanRpm = SelectFirstValidFan(flat),
        };
    }

    /// <summary>
    /// CPU min/max/avg (User Story 5, FR-021): aggregates per-core readings when Intel's hybrid
    /// P-Core/E-Core naming is present; otherwise falls back to every genuine temperature
    /// reading under <c>lhm_cpu_temperature_celsius</c>, excluding only the confirmed
    /// non-temperature "Distance to TjMax" sensors (research.md R-13). The fallback path is
    /// unverified against real AMD/non-hybrid-Intel output -- see spec.md Assumptions.
    /// </summary>
    public static (double? Min, double? Max, double? Avg) SelectCpuCoreStats(IReadOnlyList<FlatMetric> flat)
    {
        var samples = flat.Where(f => f.Name == "lhm_cpu_temperature_celsius").ToList();

        var coreValues = samples
            .Where(f => f.Labels.TryGetValue("sensorName", out var n) && CpuCorePattern().IsMatch(n.Trim()))
            .Select(f => f.Value)
            .Where(IsValidTemperature)
            .ToList();

        if (coreValues.Count == 0)
        {
            coreValues = samples
                .Where(f => f.Labels.TryGetValue("sensorName", out var n) &&
                            !n.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Value)
                .Where(IsValidTemperature)
                .ToList();
        }

        return Aggregate(coreValues);
    }

    /// <summary>
    /// GPU min/max/avg (User Story 5, FR-022): every genuine temperature sensor LHM reports for
    /// this GPU in the batch (Core, Hot Spot, Memory Junction, VR sensors, etc. -- research.md
    /// F-05 confirms none of these share CPU's "Distance to TjMax" pitfall).
    /// </summary>
    public static (double? Min, double? Max, double? Avg) SelectGpuStats(IReadOnlyList<FlatMetric> flat)
    {
        var values = flat
            .Where(f => GpuTemperatureMetricNames.Contains(f.Name))
            .Select(f => f.Value)
            .Where(IsValidTemperature)
            .ToList();
        return Aggregate(values);
    }

    private static double? SelectByNamePriority(IReadOnlyList<FlatMetric> samples, IReadOnlyList<string> priority)
    {
        foreach (var name in priority)
        {
            var values = samples
                .Where(f => SensorNameMatches(f, name))
                .Select(f => f.Value)
                .Where(IsValidTemperature)
                .ToList();
            if (values.Count > 0) return values.Max();
        }
        return null;
    }

    private static double? SelectByPrefix(IReadOnlyList<FlatMetric> samples, string prefix)
    {
        var values = samples
            .Where(f => f.Labels.TryGetValue("sensorName", out var n) &&
                        n.Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Value)
            .Where(IsValidTemperature)
            .ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    private static double? SelectFirstValidFan(IReadOnlyList<FlatMetric> flat)
    {
        foreach (var f in flat)
            if (f.Name == "lhm_motherboard_fan_rpm" && double.IsFinite(f.Value) && f.Value >= 0)
                return f.Value;
        return null;
    }

    private static (double? Min, double? Max, double? Avg) Aggregate(IReadOnlyList<double> values) =>
        values.Count > 0 ? (values.Min(), values.Max(), values.Average()) : (null, null, null);

    private static bool SensorNameMatches(FlatMetric f, string expected) =>
        f.Labels.TryGetValue("sensorName", out var actual) &&
        string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
