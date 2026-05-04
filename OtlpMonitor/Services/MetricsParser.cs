using OtlpMonitor.Models;
using OtlpMonitor.Models.Display;
using OtlpMonitor.Models.Otlp;

namespace OtlpMonitor.Services;

public class MetricsParser
{
    private readonly Dictionary<string, CounterTracker> _counters = new();
    private double _memTotalBytes;

    public MetricsSnapshot Parse(OtlpExportRequest request)
    {
        var flat = FlattenMetrics(request);

        return new MetricsSnapshot
        {
            ReceivedAt = DateTimeOffset.Now,
            Memory = ParseMemory(flat),
            Cpu = ParseCpu(flat),
            System = ParseSystem(flat),
            Gpu = ParseGpu(flat),
            Temperatures = ParseTemperatures(flat),
            Disks = ParseDisks(flat),
            Network = ParseNetwork(flat),
            TopMemoryProcesses = ParseTopMemoryProcesses(flat),
            TopCpuProcesses = ParseTopCpuProcesses(flat),
            TopNetworkProcesses = [],
        };
    }

    // --- Flatten ---

    private static List<FlatMetric> FlattenMetrics(OtlpExportRequest request)
    {
        var result = new List<FlatMetric>();
        foreach (var rm in request.ResourceMetrics ?? [])
        foreach (var sm in rm.ScopeMetrics ?? [])
        foreach (var m in sm.Metrics ?? [])
        {
            var points = (m.Gauge?.DataPoints ?? m.Sum?.DataPoints) ?? [];

            // Prometheus→OTLP変換でcumulativeカウンターがGaugeになる場合がある。
            // _total サフィックスのGaugeは累積カウンターとして扱う。
            bool isCumulative = m.Sum?.AggregationTemporality == 2 ||
                                 (m.Gauge != null && m.Name.EndsWith("_total"));

            foreach (var dp in points)
            {
                var labels = (dp.Attributes ?? [])
                    .ToDictionary(kv => kv.Key, kv => kv.Value?.AsString() ?? string.Empty);
                result.Add(new FlatMetric(m.Name, labels, dp.GetValue(), dp.GetTimeNano(), isCumulative));
            }
        }
        return result;
    }

    // --- Memory ---

    private MemoryInfo ParseMemory(List<FlatMetric> flat)
    {
        double total = GetGauge(flat, "windows_memory_physical_total_bytes");
        double available = GetGauge(flat, "windows_memory_available_bytes");
        double commitLimit = GetGauge(flat, "windows_memory_commit_limit");
        double committed = GetGauge(flat, "windows_memory_committed_bytes");

        if (total > 0) _memTotalBytes = total;
        if (_memTotalBytes <= 0) _memTotalBytes = 1;

        return new MemoryInfo
        {
            AvailableBytes = available,
            TotalBytes = _memTotalBytes,
            PagefileTotalBytes = commitLimit,
            PagefileUsedBytes = Math.Max(0, committed),
        };
    }

    // --- CPU ---

    private CpuInfo ParseCpu(List<FlatMetric> flat)
    {
        double? dpc    = GetCpuModeRate(flat, "windows_cpu_time_total", "mode", "dpc");
        double? intr   = GetCpuModeRate(flat, "windows_cpu_time_total", "mode", "interrupt");
        double? user   = GetCpuModeRate(flat, "windows_cpu_time_total", "mode", "user");
        double? priv   = GetCpuModeRate(flat, "windows_cpu_time_total", "mode", "privileged");
        double? idle   = GetCpuModeRate(flat, "windows_cpu_time_total", "mode", "idle");
        double? c1     = GetCpuModeRate(flat, "windows_cpu_cstate_seconds_total", "state", "c1");
        double? c2     = GetCpuModeRate(flat, "windows_cpu_cstate_seconds_total", "state", "c2");
        double? c3     = GetCpuModeRate(flat, "windows_cpu_cstate_seconds_total", "state", "c3");
        double? temp   = GetNullableGauge(flat, "windows_thermalzone_temperature_celsius");

        return new CpuInfo
        {
            TemperatureCelsius = temp,
            C1Percent = c1,
            C2Percent = c2,
            C3Percent = c3,
            DpcPercent = dpc,
            InterruptPercent = intr,
            UserPercent = user,
            SystemPercent = priv,
            IdlePercent = idle,
        };
    }

    private double? GetCpuModeRate(List<FlatMetric> flat, string metricName, string labelKey, string labelVal)
    {
        var points = flat.Where(f => f.Name == metricName &&
                                     f.IsCumulative &&
                                     f.Labels.TryGetValue(labelKey, out var v) && v == labelVal).ToList();
        if (points.Count == 0) return null;

        double totalRate = 0;
        foreach (var p in points)
        {
            var coreId = p.Labels.GetValueOrDefault("core", "");
            var key = $"{metricName}|core={coreId}|{labelKey}={labelVal}";
            var rate = GetOrCreateTracker(key).UpdateAndGetRate(p.Value, p.TimeNano);
            if (rate.HasValue) totalRate += rate.Value * 100;
        }
        return Math.Clamp(totalRate / Math.Max(points.Count, 1), 0, 100);
    }

    // --- System Objects ---

    private static SystemObjectsInfo ParseSystem(List<FlatMetric> flat) => new()
    {
        ProcessCount = (long)GetGauge(flat, "windows_system_processes"),
        ThreadCount  = (long)GetGauge(flat, "windows_system_threads"),
        HandleCount  = (long)flat.Where(f => f.Name == "windows_process_handles").Sum(f => f.Value),
        QueueLenProc = GetNullableGauge(flat, "windows_system_processor_queue_length"),
    };

    // --- GPU ---

    private GpuInfo ParseGpu(List<FlatMetric> flat)
    {
        var gpuPoints = flat.Where(f => f.Name == "windows_gpu_engine_time_percent").ToList();
        double overall = gpuPoints.Count > 0 ? gpuPoints.Average(p => p.Value) : 0;
        return new GpuInfo { OverallPercent = Math.Clamp(overall, 0, 100) };
    }

    // --- Temperatures ---

    private static HardwareTemperatures ParseTemperatures(List<FlatMetric> flat)
    {
        // windows_thermalzone_temperature_celsius: システム温度帯域ゾーン
        // CPU Package温度は現バージョンのwindows_exporterでは未提供
        double? thermalZone = GetNullableGauge(flat, "windows_thermalzone_temperature_celsius");

        return new HardwareTemperatures
        {
            CpuPackageCelsius = thermalZone,
            SysCelsius        = thermalZone,
        };
    }

    // --- Disks ---

    private DiskInfo[] ParseDisks(List<FlatMetric> flat)
    {
        var volumes = flat
            .Where(f => f.Labels.ContainsKey("volume"))
            .Select(f => f.Labels["volume"])
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        return volumes.Select(vol => new DiskInfo
        {
            DriveLetter      = vol,
            ReadBytesPerSec  = GetCounterRate(flat, "windows_logical_disk_read_bytes_total", "volume", vol),
            WriteBytesPerSec = GetCounterRate(flat, "windows_logical_disk_write_bytes_total", "volume", vol),
            IopsRead         = GetCounterRate(flat, "windows_logical_disk_reads_total", "volume", vol),
            IopsWrite        = GetCounterRate(flat, "windows_logical_disk_writes_total", "volume", vol),
        }).ToArray();
    }

    // --- Network ---

    private NetworkInfo ParseNetwork(List<FlatMetric> flat)
    {
        var nics = flat.Where(f => f.Labels.ContainsKey("nic"))
                       .Select(f => f.Labels["nic"]).Distinct().ToList();
        string nicName = nics.FirstOrDefault() ?? string.Empty;

        double totalSend = 0, totalRecv = 0;
        foreach (var nic in nics)
        {
            totalSend += GetCounterRate(flat, "windows_net_bytes_sent_total", "nic", nic) ?? 0;
            totalRecv += GetCounterRate(flat, "windows_net_bytes_received_total", "nic", nic) ?? 0;
        }
        return new NetworkInfo
        {
            InterfaceName         = nicName,
            TotalSendBytesPerSec  = totalSend,
            TotalRecvBytesPerSec  = totalRecv,
        };
    }

    // --- Process lists ---

    private ProcessSnapshot[] ParseTopMemoryProcesses(List<FlatMetric> flat) =>
        flat.Where(f => f.Name == "windows_process_working_set_bytes")
            .GroupBy(f => (
                f.Labels.GetValueOrDefault("process", ""),
                f.Labels.GetValueOrDefault("process_id", "0")))
            .Select(g => new ProcessSnapshot
            {
                Name         = g.Key.Item1,
                Pid          = long.TryParse(g.Key.Item2, out var p) ? p : 0,
                MemoryBytes  = g.Max(x => x.Value),
            })
            .OrderByDescending(p => p.MemoryBytes)
            .Take(5).ToArray();

    private ProcessSnapshot[] ParseTopCpuProcesses(List<FlatMetric> flat)
    {
        var result = new List<ProcessSnapshot>();
        var groups = flat.Where(f => f.Name == "windows_process_cpu_time_total" && f.IsCumulative)
                         .GroupBy(f => (
                             f.Labels.GetValueOrDefault("process", ""),
                             f.Labels.GetValueOrDefault("process_id", "0")));

        foreach (var g in groups)
        {
            double totalRate = 0;
            foreach (var p in g)
            {
                var mode = p.Labels.GetValueOrDefault("mode", "");
                var key = $"proc_cpu|{g.Key.Item1}|{g.Key.Item2}|{mode}";
                var rate = GetOrCreateTracker(key).UpdateAndGetRate(p.Value, p.TimeNano);
                if (rate.HasValue) totalRate += rate.Value * 100;
            }
            result.Add(new ProcessSnapshot
            {
                Name       = g.Key.Item1,
                Pid        = long.TryParse(g.Key.Item2, out var pid) ? pid : 0,
                CpuPercent = Math.Clamp(totalRate, 0, 400),
            });
        }
        return result.OrderByDescending(p => p.CpuPercent).Take(5).ToArray();
    }

    // --- Helpers ---

    private static double GetGauge(List<FlatMetric> flat, string name) =>
        flat.FirstOrDefault(f => f.Name == name)?.Value ?? 0;

    private static double? GetNullableGauge(List<FlatMetric> flat, string name,
        string? labelKey = null, string? labelVal = null)
    {
        var match = flat.FirstOrDefault(f => f.Name == name &&
            (labelKey == null || (f.Labels.TryGetValue(labelKey, out var v) && v == labelVal)));
        return match?.Value is double d && d > 0 ? d : null;
    }

    private double? GetCounterRate(List<FlatMetric> flat, string name,
        string? labelKey = null, string? labelVal = null)
    {
        var match = flat.FirstOrDefault(f => f.Name == name && f.IsCumulative &&
            (labelKey == null || (f.Labels.TryGetValue(labelKey, out var v) && v == labelVal)));
        if (match is null) return null;
        var key = $"{name}|{labelKey}={labelVal}";
        return GetOrCreateTracker(key).UpdateAndGetRate(match.Value, match.TimeNano);
    }

    private CounterTracker GetOrCreateTracker(string key)
    {
        if (!_counters.TryGetValue(key, out var tracker))
        {
            tracker = new CounterTracker();
            _counters[key] = tracker;
        }
        return tracker;
    }

    private record FlatMetric(
        string Name,
        Dictionary<string, string> Labels,
        double Value,
        ulong TimeNano,
        bool IsCumulative);
}
