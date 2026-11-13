using System.Management;
using WindowsMetrics.Models;
using WindowsMetrics.Models.Display;
using WindowsMetrics.Services.Sensors;

namespace WindowsMetrics.Services;

public class MetricsParser
{
    /// <summary>
    /// Name prefixes this parser actually consumes. Scrapers pass this to
    /// <see cref="Parsing.PrometheusTextParser.Parse"/> so unrelated series (e.g. the bulk of
    /// windows_exporter's process/gpu memory metrics we never read) are dropped before they
    /// ever become a <see cref="FlatMetric"/>.
    /// See specs/004-windowsmetrics-github-publish/contracts/provider-metrics.md.
    /// </summary>
    public static readonly IReadOnlyCollection<string> RequiredMetricPrefixes =
    [
        "windows_cpu_",
        "windows_memory_",
        "windows_process_working_set_bytes",
        "windows_process_cpu_time_seconds_total",
        "windows_logical_disk_",
        "windows_net_bytes_",
        "windows_gpu_engine_time_seconds_total",
        "windows_thermalzone_temperature_celsius",
        "lhm_memory_temperature_celsius",
        "lhm_storage_temperature_celsius",
        "lhm_cpu_temperature_celsius",
        "lhm_gpunvidia_temperature_celsius",
        "lhm_gpuamd_temperature_celsius",
        "lhm_gpuintel_temperature_celsius",
        "lhm_motherboard_temperature_celsius",
        "lhm_motherboard_fan_rpm",
    ];

    private static readonly string[] GpuTemperatureMetricNames =
    [
        "lhm_gpunvidia_temperature_celsius", "lhm_gpuamd_temperature_celsius", "lhm_gpuintel_temperature_celsius",
    ];

    private readonly Dictionary<string, CounterTracker> _counters = new();
    private readonly Dictionary<string, double> _lastValidRate = new();
    private readonly Dictionary<long, string> _pidToName = new();
    private readonly Dictionary<string, string> _driveToModel;
    private readonly Dictionary<string, double> _modelToTemp = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _dimmIdToTempC = new(StringComparer.OrdinalIgnoreCase);
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

    // Holds the most recently received value for each domain (used for partial updates)
    private MemoryInfo _latestMemory = new();
    private CpuInfo _latestCpu = new();
    private GpuInfo _latestGpu = new();
    private HardwareTemperatures _latestTemperatures = new();
    private DiskInfo[] _latestDisks = [];
    private NetworkInfo _latestNetwork = new();
    private ProcessSnapshot[] _latestTopMemProc = [];
    private ProcessSnapshot[] _latestTopCpuProc = [];

    // Per-source temperature values, held separately from _latestCpu/_latestGpu so a batch from
    // one provider never erases a reading the other provider supplied (see research.md R-03).
    // LHM's CPU package reading takes priority over the ACPI thermal zone fallback at composition
    // time (see the end of this method), not here.
    private double? _thermalZoneC;
    private double? _lhmCpuPackageC;
    private double? _gpuCoreC;
    private (double? Min, double? Max, double? Avg) _cpuCoreStats;
    private (double? Min, double? Max, double? Avg) _gpuStats;

    public MetricsSnapshot Parse(IReadOnlyList<FlatMetric> flat)
    {
        // Update only the domains present in this batch; keep the previous value for the rest
        if (flat.Any(f => f.Name == "lhm_memory_temperature_celsius"))
            UpdateDimmTemperatures(flat);
        if (flat.Any(f => f.Name.StartsWith("windows_memory_") || f.Name == "lhm_memory_temperature_celsius"))
            _latestMemory = ParseMemory(flat);
        if (flat.Any(f => f.Name.StartsWith("windows_cpu_")))
            _latestCpu = ParseCpu(flat);
        if (flat.Any(f => f.Name == "windows_thermalzone_temperature_celsius"))
            _thermalZoneC = GetNullableGauge(flat, "windows_thermalzone_temperature_celsius");
        if (flat.Any(f => f.Name == "lhm_cpu_temperature_celsius"))
        {
            _lhmCpuPackageC = HardwareSensorSelector.SelectCpuPackage(flat);
            _cpuCoreStats = HardwareSensorSelector.SelectCpuCoreStats(flat);
        }
        if (flat.Any(f => f.Name == "windows_gpu_engine_time_seconds_total"))
            _latestGpu = ParseGpu(flat);
        if (flat.Any(f => GpuTemperatureMetricNames.Contains(f.Name)))
        {
            _gpuCoreC = HardwareSensorSelector.SelectGpuCore(flat);
            _gpuStats = HardwareSensorSelector.SelectGpuStats(flat);
        }
        if (flat.Any(f => f.Name == "lhm_motherboard_temperature_celsius" || f.Name == "lhm_motherboard_fan_rpm"))
            _latestTemperatures = HardwareSensorSelector.SelectBoard(flat);
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
        if (flat.Any(f => f.Name == "windows_process_cpu_time_seconds_total"))
        {
            UpdatePidNames(flat, "windows_process_cpu_time_seconds_total");
            _latestTopCpuProc = ParseTopCpuProcesses(flat);
        }

        return new MetricsSnapshot
        {
            ReceivedAt = DateTimeOffset.Now,
            Memory = _latestMemory,
            // LHM's CPU package temperature wins whenever it has ever been observed; otherwise
            // fall back to the ACPI thermal zone (see spec.md FR-007, contracts/hardware-
            // temperature-metrics.md "CPU card's temperature").
            Cpu = _latestCpu with
            {
                TemperatureCelsius = _lhmCpuPackageC ?? _thermalZoneC,
                MinTempC = _cpuCoreStats.Min,
                MaxTempC = _cpuCoreStats.Max,
                AvgTempC = _cpuCoreStats.Avg,
            },
            Gpu = _latestGpu with
            {
                TemperatureCelsius = _gpuCoreC,
                MinTempC = _gpuStats.Min,
                MaxTempC = _gpuStats.Max,
                AvgTempC = _gpuStats.Avg,
            },
            Temperatures = _latestTemperatures,
            Disks = _latestDisks,
            Network = _latestNetwork,
            TopMemoryProcesses = _latestTopMemProc,
            TopCpuProcesses = _latestTopCpuProc,
            TopNetworkProcesses = [],
        };
    }

    // --- Memory ---

    private MemoryInfo ParseMemory(IReadOnlyList<FlatMetric> flat)
    {
        double total = GetGauge(flat, "windows_memory_physical_total_bytes");
        double available = GetGauge(flat, "windows_memory_available_bytes");
        double commitLimit = GetGauge(flat, "windows_memory_commit_limit_bytes");
        double committed = GetGauge(flat, "windows_memory_committed_bytes");

        if (total > 0) _memTotalBytes = total;
        if (_memTotalBytes <= 0) _memTotalBytes = 1;

        // For a batch that has no windows_memory_* series and only lhm_memory_temperature_celsius,
        // carry over the existing latest values and replace only the temperature fields.
        bool hasWindowsMem = flat.Any(f => f.Name.StartsWith("windows_memory_"));
        double availableBytes = hasWindowsMem ? available : _latestMemory.AvailableBytes;
        double totalBytes = hasWindowsMem ? _memTotalBytes : (_latestMemory.TotalBytes > 0 ? _latestMemory.TotalBytes : _memTotalBytes);
        double pagefileTotal = hasWindowsMem ? commitLimit : _latestMemory.PagefileTotalBytes;
        double pagefileUsed = hasWindowsMem ? Math.Max(0, committed) : _latestMemory.PagefileUsedBytes;

        double? minT = null, maxT = null, avgT = null;
        int count = _dimmIdToTempC.Count;
        if (count > 0)
        {
            var values = _dimmIdToTempC.Values;
            minT = values.Min();
            maxT = values.Max();
            avgT = values.Average();
        }

        return new MemoryInfo
        {
            AvailableBytes = availableBytes,
            TotalBytes = totalBytes,
            PagefileTotalBytes = pagefileTotal,
            PagefileUsedBytes = pagefileUsed,
            MinTempC = minT,
            MaxTempC = maxT,
            AvgTempC = avgT,
            TempSampleCount = count,
        };
    }

    private void UpdateDimmTemperatures(IReadOnlyList<FlatMetric> flat)
    {
        // To avoid backfilling with stale values, whenever this batch contains DIMM temperature
        // samples, clear the dict first and repopulate it with only this batch's samples.
        _dimmIdToTempC.Clear();
        foreach (var f in flat.Where(f => f.Name == "lhm_memory_temperature_celsius"))
        {
            var key = f.Labels.GetValueOrDefault("hardwareId", "");
            if (string.IsNullOrEmpty(key))
                key = f.Labels.GetValueOrDefault("sensorName", "");
            if (string.IsNullOrEmpty(key)) continue;
            _dimmIdToTempC[key] = f.Value;
        }
    }

    // --- CPU ---

    private CpuInfo ParseCpu(IReadOnlyList<FlatMetric> flat)
    {
        double? dpc    = GetCpuModeRate(flat, "windows_cpu_time_seconds_total", "mode", "dpc");
        double? intr   = GetCpuModeRate(flat, "windows_cpu_time_seconds_total", "mode", "interrupt");
        double? user   = GetCpuModeRate(flat, "windows_cpu_time_seconds_total", "mode", "user");
        double? priv   = GetCpuModeRate(flat, "windows_cpu_time_seconds_total", "mode", "privileged");
        double? idle   = GetCpuModeRate(flat, "windows_cpu_time_seconds_total", "mode", "idle");
        double? c1     = GetCpuModeRate(flat, "windows_cpu_cstate_seconds_total", "state", "c1");
        double? c2     = GetCpuModeRate(flat, "windows_cpu_cstate_seconds_total", "state", "c2");
        double? c3     = GetCpuModeRate(flat, "windows_cpu_cstate_seconds_total", "state", "c3");

        return new CpuInfo
        {
            // TemperatureCelsius is filled in by Parse() from the LHM/thermal-zone fallback
            // chain, not here -- this batch may not even be the one that carries the temperature.
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

    private double? GetCpuModeRate(IReadOnlyList<FlatMetric> flat, string metricName, string labelKey, string labelVal)
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

    // --- GPU ---

    private GpuInfo ParseGpu(IReadOnlyList<FlatMetric> flat)
    {
        // windows_gpu_engine_time_seconds_total: cumulative seconds (total GPU time since process start)
        // Even though IsCumulative=False, it is actually a monotonically increasing counter -> compute the rate via CounterTracker
        // sum(rate_i) per engtype = GPU usage (sec/sec = fraction); *100 for percent
        var gpuPoints = flat.Where(f => f.Name == "windows_gpu_engine_time_seconds_total").ToList();
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

        return new GpuInfo { OverallPercent = overall, Processes = processes, IsUsageKnown = true };
    }

    // --- Disks ---

    private void UpdateDiskTemperatures(IReadOnlyList<FlatMetric> flat)
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

    private DiskInfo[] ParseDisks(IReadOnlyList<FlatMetric> flat)
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
            UsagePercent     = ComputeDiskUsagePercent(flat, vol),
        }).ToArray();
    }

    private double LookupDiskTemp(string volume)
    {
        var letter = volume.TrimEnd(':');
        if (!_driveToModel.TryGetValue(letter, out var model)) return 0;

        // Exact match
        if (_modelToTemp.TryGetValue(model, out var temp)) return temp;

        // Fallback for when LHM prepends a brand name, e.g. "HIKVISION HS-SSD-FUTURE 1024G"
        foreach (var kv in _modelToTemp)
        {
            if (kv.Key.Contains(model, StringComparison.OrdinalIgnoreCase) ||
                model.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return 0;
    }

    private double? ComputeDiskUsagePercent(IReadOnlyList<FlatMetric> flat, string volume)
    {
        var sizeBytes = GetNullableGauge(flat, "windows_logical_disk_size_bytes", "volume", volume);
        var freeBytes = GetNullableGauge(flat, "windows_logical_disk_free_bytes", "volume", volume);

        if (sizeBytes > 0 && freeBytes.HasValue)
            return Math.Clamp((sizeBytes.Value - freeBytes.Value) / sizeBytes.Value * 100, 0, 100);

        return null;
    }

    // --- Network ---

    private NetworkInfo ParseNetwork(IReadOnlyList<FlatMetric> flat)
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

    private ProcessSnapshot[] ParseTopMemoryProcesses(IReadOnlyList<FlatMetric> flat) =>
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

    private ProcessSnapshot[] ParseTopCpuProcesses(IReadOnlyList<FlatMetric> flat)
    {
        var result = new List<ProcessSnapshot>();
        var groups = flat.Where(f => f.Name == "windows_process_cpu_time_seconds_total" && f.IsCumulative)
                         .Where(f => f.Labels.GetValueOrDefault("process", "") is not ("Idle" or "_Total"))
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

    private void UpdatePidNames(IReadOnlyList<FlatMetric> flat, string metricName)
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

    private static double GetGauge(IReadOnlyList<FlatMetric> flat, string name) =>
        flat.FirstOrDefault(f => f.Name == name)?.Value ?? 0;

    private static double? GetNullableGauge(IReadOnlyList<FlatMetric> flat, string name,
        string? labelKey = null, string? labelVal = null)
    {
        var match = flat.FirstOrDefault(f => f.Name == name &&
            (labelKey == null || (f.Labels.TryGetValue(labelKey, out var v) && v == labelVal)));
        return match?.Value is double d && d > 0 ? d : null;
    }

    private double? GetCounterRate(IReadOnlyList<FlatMetric> flat, string name,
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
}
