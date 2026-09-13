namespace OtlpMonitor.Models.Display;

public class MetricsSnapshot
{
    public DateTimeOffset ReceivedAt { get; init; } = DateTimeOffset.Now;
    public MemoryInfo Memory { get; init; } = new();
    public ProcessSnapshot[] TopMemoryProcesses { get; init; } = [];
    public CpuInfo Cpu { get; init; } = new();
    public SystemObjectsInfo System { get; init; } = new();
    public ProcessSnapshot[] TopCpuProcesses { get; init; } = [];
    public GpuInfo Gpu { get; init; } = new();
    public NetworkProcessSnapshot[] TopNetworkProcesses { get; init; } = [];
    public HardwareTemperatures Temperatures { get; init; } = new();
    public DiskInfo[] Disks { get; init; } = [];
    public NetworkInfo Network { get; init; } = new();
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

public class CpuInfo
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
}

public class SystemObjectsInfo
{
    public long ProcessCount { get; init; }
    public long ThreadCount { get; init; }
    public long HandleCount { get; init; }
    public double? QueueLenProc { get; init; }
    public double? QueueLenDisk { get; init; }
}

public class GpuInfo
{
    public double OverallPercent { get; init; }
    public GpuProcessSnapshot[] Processes { get; init; } = [];
}

public class GpuProcessSnapshot
{
    public string Name { get; init; } = string.Empty;
    public long Pid { get; init; }
    public double UsagePercent { get; init; }
    public double? MemoryBytes { get; init; }
}

public class HardwareTemperatures
{
    public double? CpuPackageCelsius { get; init; }
    public double? SysCelsius { get; init; }
    public double? MosCelsius { get; init; }
    public double? PchCelsius { get; init; }
    public double? SockCelsius { get; init; }
    public double? PcieCelsius { get; init; }
    public double? M2Celsius { get; init; }
    public double? FanRpm { get; init; }
    public bool IsCpuOverheat => CpuPackageCelsius.HasValue && CpuPackageCelsius.Value >= 80;
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
