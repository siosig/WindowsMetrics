using System.Management;
using OtlpMonitor.Models;
using OtlpMonitor.Models.Display;
using OtlpMonitor.Models.Otlp;

namespace OtlpMonitor.Services;

public class MetricsParser
{
    private readonly Dictionary<string, CounterTracker> _counters = new();
    private readonly Dictionary<string, double> _lastValidRate = new();
    private readonly Dictionary<long, string> _pidToName = new();
    private readonly Dictionary<string, string> _driveToModel;
    private readonly Dictionary<string, double> _modelToTemp = new(StringComparer.OrdinalIgnoreCase);
    private double _memTotalBytes;

    public MetricsParser()
    {
        _driveToModel = BuildDriveToModelMap();
    }

    private static Dictionary<string, string> BuildDriveToModelMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var diskModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var q1 = new ManagementObjectSearcher("SELECT DeviceID, Model FROM Win32_DiskDrive");
            foreach (ManagementObject d in q1.Get())
                diskModels[(string)d["DeviceID"]] = ((string)d["Model"]).Trim();

            var partToDisk = new Dictionary<string, string>();
            using var q2 = new ManagementObjectSearcher(
                "SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition");
            foreach (ManagementObject r in q2.Get())
            {
                var diskId = ExtractWmiDeviceId((string)r["Antecedent"]).Replace("\\\\", "\\");
                var partId = ExtractWmiDeviceId((string)r["Dependent"]);
                if (!string.IsNullOrEmpty(diskId) && !string.IsNullOrEmpty(partId))
                    partToDisk[partId] = diskId;
            }

            using var q3 = new ManagementObjectSearcher(
                "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
            foreach (ManagementObject r in q3.Get())
            {
                var partId = ExtractWmiDeviceId((string)r["Antecedent"]);
                var logDev = ExtractWmiDeviceId((string)r["Dependent"]);
                var letter = logDev.TrimEnd(':');
                if (!string.IsNullOrEmpty(letter) && partToDisk.TryGetValue(partId, out var diskId)
                    && diskModels.TryGetValue(diskId, out var model))
                    result[letter] = model;
            }
        }
        catch { }
        return result;
    }

    private static string ExtractWmiDeviceId(string wmiPath)
    {
        var m = System.Text.RegularExpressions.Regex.Match(wmiPath, @"DeviceID=""([^""]+)""");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    // 各ドメインの最後に受け取った値を保持（部分更新用）
    private MemoryInfo _latestMemory = new();
    private CpuInfo _latestCpu = new();
    private SystemObjectsInfo _latestSystem = new();
    private GpuInfo _latestGpu = new();
    private HardwareTemperatures _latestTemperatures = new();
    private DiskInfo[] _latestDisks = [];
    private NetworkInfo _latestNetwork = new();
    private ProcessSnapshot[] _latestTopMemProc = [];
    private ProcessSnapshot[] _latestTopCpuProc = [];

    public MetricsSnapshot Parse(OtlpExportRequest request)
    {
        var flat = FlattenMetrics(request);

        // 含まれているドメインだけ更新し、他は最後の値を維持する
        if (flat.Any(f => f.Name.StartsWith("windows_memory_")))
            _latestMemory = ParseMemory(flat);
        if (flat.Any(f => f.Name.StartsWith("windows_cpu_") || f.Name == "windows_thermalzone_temperature_celsius"))
            _latestCpu = ParseCpu(flat);
        if (flat.Any(f => f.Name.StartsWith("windows_system_") || f.Name == "windows_process_handles"))
            _latestSystem = ParseSystem(flat);
        if (flat.Any(f => f.Name == "windows_gpu_engine_time_seconds"))
            _latestGpu = ParseGpu(flat);
        if (flat.Any(f => f.Name == "windows_thermalzone_temperature_celsius"))
            _latestTemperatures = ParseTemperatures(flat);
        if (flat.Any(f => f.Name == "lhm_storage_temperature_celsius"))
            UpdateDiskTemperatures(flat);

        if (flat.Any(f => f.Labels.ContainsKey("volume")))
            _latestDisks = ParseDisks(flat);
        if (flat.Any(f => f.Labels.ContainsKey("nic")))
            _latestNetwork = ParseNetwork(flat);
        if (flat.Any(f => f.Name == "windows_process_working_set_bytes"))
        {
            UpdatePidNames(flat, "windows_process_working_set_bytes");
            _latestTopMemProc = ParseTopMemoryProcesses(flat);
        }
        if (flat.Any(f => f.Name == "windows_process_cpu_time_total"))
        {
            UpdatePidNames(flat, "windows_process_cpu_time_total");
            _latestTopCpuProc = ParseTopCpuProcesses(flat);
        }

        return new MetricsSnapshot
        {
            ReceivedAt = DateTimeOffset.Now,
            Memory = _latestMemory,
            Cpu = _latestCpu,
            System = _latestSystem,
            Gpu = _latestGpu,
            Temperatures = _latestTemperatures,
            Disks = _latestDisks,
            Network = _latestNetwork,
            TopMemoryProcesses = _latestTopMemProc,
            TopCpuProcesses = _latestTopCpuProc,
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
        int validCount = 0;
        foreach (var p in points)
        {
            var coreId = p.Labels.GetValueOrDefault("core", "");
            var key = $"{metricName}|core={coreId}|{labelKey}={labelVal}";
            var rate = GetOrCreateTracker(key).UpdateAndGetRate(p.Value, p.TimeNano);
            if (rate.HasValue) { totalRate += rate.Value * 100; validCount++; }
        }
        if (validCount == 0) return null;
        return Math.Clamp(totalRate / validCount, 0, 100);
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
        // windows_gpu_engine_time_seconds: 累積秒数（プロセス起動からの合計GPU時間）
        // IsCumulative=False でも実態は単調増加カウンター → CounterTracker でレートを計算
        // sum(rate_i) per engtype = GPU使用率 (sec/sec = fraction)、*100でパーセント
        var gpuPoints = flat.Where(f => f.Name == "windows_gpu_engine_time_seconds").ToList();
        if (gpuPoints.Count == 0) return new GpuInfo { OverallPercent = _latestGpu.OverallPercent };

        var byEngine = new Dictionary<string, double>();
        var byPid    = new Dictionary<string, double>();
        int validRates = 0;
        foreach (var p in gpuPoints)
        {
            var engtype = p.Labels.GetValueOrDefault("engtype", "");
            var pid     = p.Labels.GetValueOrDefault("process_id", "");
            var luid    = p.Labels.GetValueOrDefault("luid", "");
            var key     = $"gpu|{luid}|{engtype}|{pid}";
            var rate    = GetOrCreateTracker(key).UpdateAndGetRate(p.Value, p.TimeNano);
            if (rate.HasValue)
            {
                validRates++;
                byEngine[engtype] = byEngine.GetValueOrDefault(engtype, 0) + rate.Value;
                byPid[pid]        = byPid.GetValueOrDefault(pid, 0) + rate.Value;
            }
        }

        if (validRates == 0) return new GpuInfo { OverallPercent = _latestGpu.OverallPercent, Processes = _latestGpu.Processes };
        double overall = Math.Clamp(byEngine.Values.Max() * 100.0, 0, 100);

        var processes = byPid
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv =>
            {
                long pid = long.TryParse(kv.Key, out var p) ? p : 0;
                string name = _pidToName.GetValueOrDefault(pid, kv.Key);
                return new GpuProcessSnapshot
                {
                    Name         = name,
                    Pid          = pid,
                    UsagePercent = Math.Clamp(kv.Value * 100.0, 0, 400),
                };
            })
            .ToArray();

        return new GpuInfo { OverallPercent = overall, Processes = processes };
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

    private void UpdateDiskTemperatures(List<FlatMetric> flat)
    {
        foreach (var f in flat.Where(f => f.Name == "lhm_storage_temperature_celsius"))
        {
            var sensor = f.Labels.GetValueOrDefault("sensorName", "");
            if (sensor != "Composite Temperature" && sensor != "Temperature") continue;
            var hw = f.Labels.GetValueOrDefault("hardwareName", "").Trim();
            if (!string.IsNullOrEmpty(hw))
                _modelToTemp[hw] = f.Value;
        }
    }

    private DiskInfo[] ParseDisks(List<FlatMetric> flat)
    {
        var volumes = flat
            .Where(f => f.Labels.ContainsKey("volume"))
            .Select(f => f.Labels["volume"])
            .Where(v => v.Length == 2 && char.IsLetter(v[0]) && v[1] == ':')
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        return volumes.Select(vol => new DiskInfo
        {
            DriveLetter      = vol,
            TemperatureCelsius = LookupDiskTemp(vol),
            ReadBytesPerSec  = GetCounterRate(flat, "windows_logical_disk_read_bytes_total", "volume", vol),
            WriteBytesPerSec = GetCounterRate(flat, "windows_logical_disk_write_bytes_total", "volume", vol),
            IopsRead         = GetCounterRate(flat, "windows_logical_disk_reads_total", "volume", vol),
            IopsWrite        = GetCounterRate(flat, "windows_logical_disk_writes_total", "volume", vol),
        }).ToArray();
    }

    private double LookupDiskTemp(string volume)
    {
        var letter = volume.TrimEnd(':');
        if (_driveToModel.TryGetValue(letter, out var model) && _modelToTemp.TryGetValue(model, out var temp))
            return temp;
        return 0;
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

    private void UpdatePidNames(List<FlatMetric> flat, string metricName)
    {
        foreach (var f in flat.Where(f => f.Name == metricName))
        {
            if (f.Labels.TryGetValue("process", out var name) &&
                f.Labels.TryGetValue("process_id", out var pidStr) &&
                long.TryParse(pidStr, out var pid))
            {
                _pidToName[pid] = name;
            }
        }
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
        var rate = GetOrCreateTracker(key).UpdateAndGetRate(match.Value, match.TimeNano);
        if (rate.HasValue)
        {
            _lastValidRate[key] = rate.Value;
            return rate;
        }
        return _lastValidRate.TryGetValue(key, out var cached) ? cached : null;
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
