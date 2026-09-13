namespace WindowsMetrics.Models.Display;

using System.Linq;
using WindowsMetrics.Models;

public sealed record MetricsSnapshot
{
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;
    public MemoryInfo Memory { get; init; } = new();
    public ProcessSnapshot[] TopMemoryProcesses { get; init; } = [];
    public CpuInfo Cpu { get; init; } = new();
    public ProcessSnapshot[] TopCpuProcesses { get; init; } = [];
    public GpuInfo Gpu { get; init; } = new();
    public NetworkProcessSnapshot[] TopNetworkProcesses { get; init; } = [];
    public HardwareTemperatures Temperatures { get; init; } = new();
    public DiskInfo[] Disks { get; init; } = [];
    public NetworkInfo Network { get; init; } = new();

    /// <summary>Per-provider connection health as of this snapshot. Empty before the first scrape.</summary>
    public IReadOnlyDictionary<MetricsProvider, ProviderHealth> ProviderStates { get; init; } =
        new Dictionary<MetricsProvider, ProviderHealth>();

    /// <summary>
    /// Which provider's scrape produced this snapshot and whether it succeeded. Null for
    /// snapshots not produced by <see cref="Services.Scraping.ScrapeHostedService"/> (e.g. built
    /// directly by a unit test) -- such snapshots must never advance a trend series.
    /// </summary>
    public SnapshotOrigin? Origin { get; init; }
}

public class MemoryInfo
{
    public double AvailableBytes { get; init; }
    public double TotalBytes { get; init; }
    public double PagefileTotalBytes { get; init; }
    public double PagefileUsedBytes { get; init; }

    public double? MinTempC { get; init; }
    public double? MaxTempC { get; init; }
    public double? AvgTempC { get; init; }
    public int TempSampleCount { get; init; }

    public double UsedBytes => TotalBytes - AvailableBytes;
    public double UsagePercent => TotalBytes > 0 ? Math.Clamp(UsedBytes / TotalBytes * 100, 0, 100) : 0;
    public double PagefilePercent => PagefileTotalBytes > 0 ? Math.Clamp(PagefileUsedBytes / PagefileTotalBytes * 100, 0, 100) : 0;
    public bool IsWarning => UsagePercent >= 80;

    /// <summary>
    /// Whether <see cref="TotalBytes"/> reflects a real reading rather than
    /// <see cref="Services.MetricsParser"/>'s placeholder of 1 byte used before the first
    /// windows_exporter memory batch arrives.
    /// </summary>
    public bool IsUsageKnown => TotalBytes > 1;
}

public class ProcessSnapshot
{
    public string Name { get; init; } = string.Empty;
    public long Pid { get; init; }
    public double? CpuPercent { get; init; }
    public double? MemoryBytes { get; init; }
    public double? VirtualMemoryBytes { get; init; }
    public double? GpuPercent { get; init; }
    public double? NetworkSendBytesPerSec { get; init; }
    public double? NetworkRecvBytesPerSec { get; init; }
}

public sealed record CpuInfo
{
    public double? TemperatureCelsius { get; init; }
    public double? C1Percent { get; init; }
    public double? C2Percent { get; init; }
    public double? C3Percent { get; init; }
    public double? DpcPercent { get; init; }
    public double? InterruptPercent { get; init; }
    public double? UserPercent { get; init; }
    public double? SystemPercent { get; init; }
    public double? IdlePercent { get; init; }
    public double TotalUsagePercent => IdlePercent.HasValue ? Math.Clamp(100.0 - IdlePercent.Value, 0, 100) : 0;

    /// <summary>Whether a usage rate could be computed yet (false on the very first sample).</summary>
    public bool IsUsageKnown => IdlePercent.HasValue;

    /// <summary>
    /// Min/max/average across per-core temperature readings, selected by
    /// <see cref="Services.Sensors.HardwareSensorSelector.SelectCpuCoreStats"/> (User Story 5).
    /// </summary>
    public double? MinTempC { get; init; }
    public double? MaxTempC { get; init; }
    public double? AvgTempC { get; init; }
}

public sealed record GpuInfo
{
    public double OverallPercent { get; init; }
    public GpuProcessSnapshot[] Processes { get; init; } = [];

    /// <summary>GPU core temperature in Celsius, selected by <see cref="Services.Sensors.HardwareSensorSelector.SelectGpuCore"/>.</summary>
    public double? TemperatureCelsius { get; init; }

    /// <summary>
    /// Whether at least one GPU engine rate could be computed in the batch that produced this
    /// value (false on the very first sample, since a rate needs two readings).
    /// </summary>
    public bool IsUsageKnown { get; init; }

    /// <summary>
    /// Min/max/average across every temperature sensor LHM reports for this GPU, selected by
    /// <see cref="Services.Sensors.HardwareSensorSelector.SelectGpuStats"/> (User Story 5).
    /// </summary>
    public double? MinTempC { get; init; }
    public double? MaxTempC { get; init; }
    public double? AvgTempC { get; init; }
}

public class GpuProcessSnapshot
{
    public string Name { get; init; } = string.Empty;
    public long Pid { get; init; }
    public double UsagePercent { get; init; }
    public double? MemoryBytes { get; init; }
}

public sealed record HardwareTemperatures
{
    /// <summary>The motherboard's own CPU-area sensor (Mother row "CPU"), distinct from <see cref="CpuInfo.TemperatureCelsius"/>.</summary>
    public double? BoardCpuCelsius { get; init; }
    public double? SysCelsius { get; init; }
    public double? MosCelsius { get; init; }
    public double? PchCelsius { get; init; }
    public double? SockCelsius { get; init; }
    public double? PcieCelsius { get; init; }
    public double? M2Celsius { get; init; }
    public double? FanRpm { get; init; }

    /// <summary>
    /// Min/max/average across whichever of the seven Mother-row temperatures above currently
    /// have a value (User Story 5, FR-023). Computed on demand rather than stored, since it is
    /// fully derived from the fields already on this record.
    /// </summary>
    private IEnumerable<double> ValidReadings =>
        new[] { SysCelsius, BoardCpuCelsius, MosCelsius, PchCelsius, SockCelsius, PcieCelsius, M2Celsius }
            .Where(v => v.HasValue)
            .Select(v => v!.Value);

    public double? MinTempC => ValidReadings.Any() ? ValidReadings.Min() : null;
    public double? MaxTempC => ValidReadings.Any() ? ValidReadings.Max() : null;
    public double? AvgTempC => ValidReadings.Any() ? ValidReadings.Average() : null;
}

public class DiskInfo
{
    public string DriveLetter { get; init; } = string.Empty;
    public double TemperatureCelsius { get; init; }
    public double? IopsRead { get; init; }
    public double? IopsWrite { get; init; }
    public double? UsagePercent { get; init; }
    public double? ReadBytesPerSec { get; init; }
    public double? WriteBytesPerSec { get; init; }
}

public class NetworkInfo
{
    public string InterfaceName { get; init; } = string.Empty;
    public double TotalSendBytesPerSec { get; init; }
    public double TotalRecvBytesPerSec { get; init; }
}

public class NetworkProcessSnapshot
{
    public string Name { get; init; } = string.Empty;
    public long Pid { get; init; }
    public double SendBytesPerSec { get; init; }
    public double RecvBytesPerSec { get; init; }
}
