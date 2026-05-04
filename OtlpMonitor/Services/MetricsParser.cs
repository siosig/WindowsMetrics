using OtlpMonitor.Models;
using OtlpMonitor.Models.Display;
using OtlpMonitor.Models.Otlp;

namespace OtlpMonitor.Services;

public class MetricsParser
{
    // カウンタートラッカー: key = "metricName|labelKey=labelVal,..."
    private readonly Dictionary<string, CounterTracker> _counters = new();

    // メモリ計算に必要な補助値
    private double _memTotalBytes;

    public MetricsSnapshot Parse(OtlpExportRequest request)
    {
        var flat = FlattenMetrics(request);

        var memory = ParseMemory(flat);
        var cpu = ParseCpu(flat);
        var system = ParseSystem(flat);
        var gpu = ParseGpu(flat);
        var temps = ParseTemperatures(flat);
        var disks = ParseDisks(flat);
        var network = ParseNetwork(flat);
        var topMemProc = ParseTopMemoryProcesses(flat);
        var topCpuProc = ParseTopCpuProcesses(flat);
        var topNetProc = ParseTopNetworkProcesses(flat);

        return new MetricsSnapshot
        {
            ReceivedAt = DateTimeOffset.Now,
            Memory = memory,
            Cpu = cpu,
            System = system,
            Gpu = gpu,
            Temperatures = temps,
            Disks = disks,
            Network = network,
            TopMemoryProcesses = topMemProc,
            TopCpuProcesses = topCpuProc,
            TopNetworkProcesses = topNetProc,
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
            bool isCumulative = m.Sum?.AggregationTemporality == 2;
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
        double available = GetGauge(flat, "windows_memory_available_bytes");
        double physFree = GetGauge(flat, "windows_os_physical_memory_free_bytes");
        double pagingLimit = GetGauge(flat, "windows_os_paging_limit_bytes");

        // 合計メモリ: 一度取得できたら保持
        if (available > 0 && physFree > 0)
            _memTotalBytes = available + physFree > _memTotalBytes ? available + physFree : _memTotalBytes;
        if (_memTotalBytes <= 0) _memTotalBytes = 1;

        double pagefileUsed = pagingLimit > 0 ? pagingLimit - GetGauge(flat, "windows_os_paging_free_bytes") : 0;

        return new MemoryInfo
        {
            AvailableBytes = available,
            TotalBytes = _memTotalBytes,
            PagefileTotalBytes = pagingLimit,
            PagefileUsedBytes = Math.Max(0, pagefileUsed),
        };
    }

    // --- CPU ---

    private CpuInfo ParseCpu(List<FlatMetric> flat)
    {
        double? c1 = GetCpuModeRate(flat, "c1");
        double? c2 = GetCpuModeRate(flat, "c2");
        double? c3 = GetCpuModeRate(flat, "c3");
        double? dpc = GetCpuModeRate(flat, "dpc");
        double? intr = GetCpuModeRate(flat, "interrupt");
        double? user = GetCpuModeRate(flat, "user");
        double? sys = GetCpuModeRate(flat, "system");
        double? idle = GetCpuModeRate(flat, "idle");
        double? temp = GetNullableGauge(flat, "hardware_cpu_temperature_celsius", "sensor", "CPU Package");

        return new CpuInfo
        {
            TemperatureCelsius = temp,
            C1Percent = c1,
            C2Percent = c2,
            C3Percent = c3,
            DpcPercent = dpc,
            InterruptPercent = intr,
            UserPercent = user,
            SystemPercent = sys,
            IdlePercent = idle,
        };
    }

    private double? GetCpuModeRate(List<FlatMetric> flat, string mode)
    {
        var points = flat.Where(f => f.Name == "windows_cpu_time_total" &&
                                     f.Labels.TryGetValue("mode", out var m) && m == mode).ToList();
        if (points.Count == 0) return null;

        double totalRate = 0;
        foreach (var p in points)
        {
            var key = $"windows_cpu_time_total|core={p.Labels.GetValueOrDefault("core", "")}|mode={mode}";
            var tracker = GetOrCreateTracker(key);
            var rate = tracker.UpdateAndGetRate(p.Value, p.TimeNano);
            if (rate.HasValue) totalRate += rate.Value * 100;
        }
        return Math.Clamp(totalRate / Math.Max(points.Count, 1), 0, 100);
    }

    // --- System Objects ---

    private static SystemObjectsInfo ParseSystem(List<FlatMetric> flat) => new()
    {
        ProcessCount = (long)GetGauge(flat, "windows_os_processes"),
        ThreadCount = (long)GetGauge(flat, "windows_os_threads"),
        HandleCount = (long)GetGauge(flat, "windows_os_handle_count"),
        QueueLenProc = GetNullableGauge(flat, "windows_system_processor_queue_length"),
    };

    // --- GPU ---

    private GpuInfo ParseGpu(List<FlatMetric> flat)
    {
        var gpuPoints = flat.Where(f => f.Name == "windows_gpu_engine_time_percent").ToList();
        double overall = gpuPoints.Any() ? gpuPoints.Average(p => p.Value) : 0;
        return new GpuInfo { OverallPercent = Math.Clamp(overall, 0, 100) };
    }

    // --- Temperatures ---

    private static HardwareTemperatures ParseTemperatures(List<FlatMetric> flat) => new()
    {
        CpuPackageCelsius = GetNullableGauge(flat, "hardware_cpu_temperature_celsius", "sensor", "CPU Package"),
        SysCelsius = GetNullableGauge(flat, "hardware_motherboard_temperature_celsius", "sensor", "System"),
        MosCelsius = GetNullableGauge(flat, "hardware_motherboard_temperature_celsius", "sensor", "MOS"),
        PchCelsius = GetNullableGauge(flat, "hardware_motherboard_temperature_celsius", "sensor", "PCH"),
        SockCelsius = GetNullableGauge(flat, "hardware_motherboard_temperature_celsius", "sensor", "Socket"),
        PcieCelsius = GetNullableGauge(flat, "hardware_motherboard_temperature_celsius", "sensor", "PCIe"),
        M2Celsius = GetNullableGauge(flat, "hardware_storage_temperature_celsius", "name", "M.2"),
        FanRpm = GetNullableGauge(flat, "hardware_fan_speed_rpm"),
    };

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
            DriveLetter = vol,
            TemperatureCelsius = GetNullableGauge(flat, "hardware_storage_temperature_celsius", "name", vol.TrimEnd(':')),
            UsagePercent = GetNullableGauge(flat, "windows_logical_disk_percent_disk_time", "volume", vol),
            ReadBytesPerSec = GetCounterRate(flat, "windows_logical_disk_read_bytes_total", "volume", vol),
            WriteBytesPerSec = GetCounterRate(flat, "windows_logical_disk_write_bytes_total", "volume", vol),
            IopsRead = GetCounterRate(flat, "windows_logical_disk_reads_total", "volume", vol),
            IopsWrite = GetCounterRate(flat, "windows_logical_disk_writes_total", "volume", vol),
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
        return new NetworkInfo { InterfaceName = nicName, TotalSendBytesPerSec = totalSend, TotalRecvBytesPerSec = totalRecv };
    }

    // --- Process lists ---

    private ProcessSnapshot[] ParseTopMemoryProcesses(List<FlatMetric> flat)
    {
        var wss = flat.Where(f => f.Name == "windows_process_working_set_bytes")
                      .GroupBy(f => (f.Labels.GetValueOrDefault("process", ""), f.Labels.GetValueOrDefault("pid", "0")))
                      .Select(g => new ProcessSnapshot
                      {
                          Name = g.Key.Item1,
                          Pid = long.TryParse(g.Key.Item2, out var p) ? p : 0,
                          MemoryBytes = g.Max(x => x.Value),
                      })
                      .OrderByDescending(p => p.MemoryBytes)
                      .Take(5).ToArray();
        return wss;
    }

    private ProcessSnapshot[] ParseTopCpuProcesses(List<FlatMetric> flat)
    {
        var result = new List<ProcessSnapshot>();
        var groups = flat.Where(f => f.Name == "windows_process_cpu_time_total")
                         .GroupBy(f => (f.Labels.GetValueOrDefault("process", ""), f.Labels.GetValueOrDefault("pid", "0")));
        foreach (var g in groups)
        {
            double totalRate = 0;
            foreach (var p in g)
            {
                var key = $"proc_cpu|{g.Key.Item1}|{g.Key.Item2}";
                var rate = GetOrCreateTracker(key).UpdateAndGetRate(p.Value, p.TimeNano);
                if (rate.HasValue) totalRate += rate.Value * 100;
            }
            result.Add(new ProcessSnapshot
            {
                Name = g.Key.Item1,
                Pid = long.TryParse(g.Key.Item2, out var pid) ? pid : 0,
                CpuPercent = Math.Clamp(totalRate, 0, 100),
            });
        }
        return result.OrderByDescending(p => p.CpuPercent).Take(5).ToArray();
    }

    private NetworkProcessSnapshot[] ParseTopNetworkProcesses(List<FlatMetric> flat)
    {
        // windows_exporter はプロセス別ネットワークを提供しないため空を返す
        return [];
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

    private double? GetCounterRate(List<FlatMetric> flat, string name, string? labelKey = null, string? labelVal = null)
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

    private record FlatMetric(string Name, Dictionary<string, string> Labels, double Value, ulong TimeNano, bool IsCumulative);
}
