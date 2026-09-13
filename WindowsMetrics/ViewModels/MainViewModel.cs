using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using WindowsMetrics.Models;
using WindowsMetrics.Models.Display;
using WindowsMetrics.Models.Trends;
using WindowsMetrics.Services;

namespace WindowsMetrics.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly MetricsChannel _channel;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cts;

    // Sparkline buffer (network only -- CPU/Memory/GPU now use MetricTrends/TrendGraph instead)
    public BiDirSparklineBuffer NetworkSparkline { get; } = new(30);

    // --- Trend graphs ---
    public MetricTrends Trends { get; } = new();

    /// <summary>
    /// Bumped once whenever <see cref="Trends"/>' CPU/Memory/GPU usage series advance, so the
    /// three usage <see cref="Views.Controls.TrendGraph"/> instances know to repaint. Carries no
    /// meaning of its own.
    /// </summary>
    public int UsageTrendRevision
    {
        get => field;
        private set { field = value; OnPropertyChanged(); }
    }

    /// <summary>Same role as <see cref="UsageTrendRevision"/>, for the Mother board temperature graph.</summary>
    public int BoardTrendRevision
    {
        get => field;
        private set { field = value; OnPropertyChanged(); }
    }

    // --- Memory ---
    private double _memUsedBytes;
    public double MemUsedBytes { get => _memUsedBytes; private set => Set(ref _memUsedBytes, value); }

    private double _memTotalBytes;
    public double MemTotalBytes { get => _memTotalBytes; private set => Set(ref _memTotalBytes, value); }

    private double _memUsagePercent;
    public double MemUsagePercent { get => _memUsagePercent; private set => Set(ref _memUsagePercent, value); }

    private double _pagefileTotalBytes;
    public double PagefileTotalBytes { get => _pagefileTotalBytes; private set => Set(ref _pagefileTotalBytes, value); }

    private double _pagefileUsedBytes;
    public double PagefileUsedBytes { get => _pagefileUsedBytes; private set => Set(ref _pagefileUsedBytes, value); }

    private double _pagefilePercent;
    public double PagefilePercent { get => _pagefilePercent; private set => Set(ref _pagefilePercent, value); }

    private bool _memIsWarning;
    public bool MemIsWarning { get => _memIsWarning; private set => Set(ref _memIsWarning, value); }

    private double? _memMinTempC, _memMaxTempC, _memAvgTempC;
    public double? MemMinTempC { get => _memMinTempC; private set => Set(ref _memMinTempC, value); }
    public double? MemMaxTempC { get => _memMaxTempC; private set => Set(ref _memMaxTempC, value); }
    public double? MemAvgTempC { get => _memAvgTempC; private set => Set(ref _memAvgTempC, value); }

    // --- CPU ---
    private double? _cpuTemp;
    public double? CpuTemp { get => _cpuTemp; private set => Set(ref _cpuTemp, value); }

    private double? _cpuC1, _cpuC2, _cpuC3, _cpuDpc, _cpuIntr, _cpuTotal;
    public double? CpuC1 { get => _cpuC1; private set => Set(ref _cpuC1, value); }
    public double? CpuC2 { get => _cpuC2; private set => Set(ref _cpuC2, value); }
    public double? CpuC3 { get => _cpuC3; private set => Set(ref _cpuC3, value); }
    public double? CpuDpc { get => _cpuDpc; private set => Set(ref _cpuDpc, value); }
    public double? CpuIntr { get => _cpuIntr; private set => Set(ref _cpuIntr, value); }
    public double? CpuTotal { get => _cpuTotal; private set => Set(ref _cpuTotal, value); }

    private double? _cpuMinTempC, _cpuMaxTempC, _cpuAvgTempC;
    public double? CpuMinTempC { get => _cpuMinTempC; private set => Set(ref _cpuMinTempC, value); }
    public double? CpuMaxTempC { get => _cpuMaxTempC; private set => Set(ref _cpuMaxTempC, value); }
    public double? CpuAvgTempC { get => _cpuAvgTempC; private set => Set(ref _cpuAvgTempC, value); }

    // --- GPU ---
    private double _gpuOverall;
    public double GpuOverall { get => _gpuOverall; private set => Set(ref _gpuOverall, value); }

    public double? GpuTemp
    {
        get => field;
        private set { field = value; OnPropertyChanged(); }
    }

    private double? _gpuMinTempC, _gpuMaxTempC, _gpuAvgTempC;
    public double? GpuMinTempC { get => _gpuMinTempC; private set => Set(ref _gpuMinTempC, value); }
    public double? GpuMaxTempC { get => _gpuMaxTempC; private set => Set(ref _gpuMaxTempC, value); }
    public double? GpuAvgTempC { get => _gpuAvgTempC; private set => Set(ref _gpuAvgTempC, value); }

    private GpuProcessSnapshot[] _topGpuProc = [];
    public GpuProcessSnapshot[] TopGpuProc { get => _topGpuProc; private set => Set(ref _topGpuProc, value); }

    // --- Temperatures ---
    private HardwareTemperatures _temperatures = new();
    public HardwareTemperatures Temperatures { get => _temperatures; private set => Set(ref _temperatures, value); }

    // --- Disks ---
    private DiskInfo[] _disks = [];
    public DiskInfo[] Disks { get => _disks; private set => Set(ref _disks, value); }

    // --- Network ---
    private double _netSend, _netRecv;
    private string _netInterface = string.Empty;
    public double NetSend { get => _netSend; private set => Set(ref _netSend, value); }
    public double NetRecv { get => _netRecv; private set => Set(ref _netRecv, value); }
    public string NetInterface { get => _netInterface; private set => Set(ref _netInterface, value); }

    // --- Process Lists ---
    private ProcessSnapshot[] _topMemProc = [];
    private ProcessSnapshot[] _topCpuProc = [];
    private NetworkProcessSnapshot[] _topNetProc = [];
    public ProcessSnapshot[] TopMemProc { get => _topMemProc; private set => Set(ref _topMemProc, value); }
    public ProcessSnapshot[] TopCpuProc { get => _topCpuProc; private set => Set(ref _topCpuProc, value); }
    public NetworkProcessSnapshot[] TopNetProc { get => _topNetProc; private set => Set(ref _topNetProc, value); }

    // --- Connection status ---
    private ConnectionState _connectionState = ConnectionState.AllOffline;
    public ConnectionState ConnectionState { get => _connectionState; private set => Set(ref _connectionState, value); }

    private string _connectionTooltip = string.Empty;
    public string ConnectionTooltip { get => _connectionTooltip; private set => Set(ref _connectionTooltip, value); }

    public MainViewModel(MetricsChannel channel, Dispatcher dispatcher)
    {
        _channel = channel;
        _dispatcher = dispatcher;
    }

    public void StartConsuming(CancellationToken externalToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _ = ConsumeLoopAsync(_cts.Token);
    }

    public void StopConsuming()
    {
        _cts?.Cancel();
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        await foreach (var snapshot in _channel.Reader.ReadAllAsync(ct))
        {
            await _dispatcher.InvokeAsync(() => ApplySnapshot(snapshot)).Task.ConfigureAwait(false);
        }
    }

    private void ApplySnapshot(MetricsSnapshot s)
    {
        UpdateConnectionStatus(s.ProviderStates);
        MemUsedBytes = s.Memory.UsedBytes;
        MemTotalBytes = s.Memory.TotalBytes;
        MemUsagePercent = s.Memory.UsagePercent;
        PagefileTotalBytes = s.Memory.PagefileTotalBytes;
        PagefileUsedBytes = s.Memory.PagefileUsedBytes;
        PagefilePercent = s.Memory.PagefilePercent;
        MemIsWarning = s.Memory.IsWarning;
        MemMinTempC = s.Memory.MinTempC;
        MemMaxTempC = s.Memory.MaxTempC;
        MemAvgTempC = s.Memory.AvgTempC;

        CpuTemp = s.Cpu.TemperatureCelsius;
        CpuC1 = s.Cpu.C1Percent;
        CpuC2 = s.Cpu.C2Percent;
        CpuC3 = s.Cpu.C3Percent;
        CpuDpc = s.Cpu.DpcPercent;
        CpuIntr = s.Cpu.InterruptPercent;
        CpuTotal = s.Cpu.TotalUsagePercent;
        CpuMinTempC = s.Cpu.MinTempC;
        CpuMaxTempC = s.Cpu.MaxTempC;
        CpuAvgTempC = s.Cpu.AvgTempC;

        GpuOverall = s.Gpu.OverallPercent;
        GpuTemp = s.Gpu.TemperatureCelsius;
        GpuMinTempC = s.Gpu.MinTempC;
        GpuMaxTempC = s.Gpu.MaxTempC;
        GpuAvgTempC = s.Gpu.AvgTempC;
        TopGpuProc = s.Gpu.Processes;
        Temperatures = s.Temperatures;
        Disks = s.Disks;

        NetSend = s.Network.TotalSendBytesPerSec;
        NetRecv = s.Network.TotalRecvBytesPerSec;
        NetInterface = s.Network.InterfaceName;
        NetworkSparkline.Push(s.Network.TotalRecvBytesPerSec, s.Network.TotalSendBytesPerSec);

        TopMemProc = s.TopMemoryProcesses;
        TopCpuProc = s.TopCpuProcesses;
        TopNetProc = s.TopNetworkProcesses;

        var advanced = Trends.Record(s);
        if (advanced.HasFlag(TrendGroups.Usage)) UsageTrendRevision++;
        if (advanced.HasFlag(TrendGroups.BoardTemperature)) BoardTrendRevision++;

        OnPropertyChanged(nameof(NetworkSparkline));
        SnapshotApplied?.Invoke();
    }

    private void UpdateConnectionStatus(IReadOnlyDictionary<MetricsProvider, ProviderHealth> states)
    {
        if (states.Count == 0)
        {
            ConnectionState = ConnectionState.AllOffline;
            ConnectionTooltip = string.Empty;
            return;
        }

        var onlineCount = states.Values.Count(h => h.IsOnline);
        ConnectionState = onlineCount == states.Count
            ? ConnectionState.AllOnline
            : onlineCount == 0
                ? ConnectionState.AllOffline
                : ConnectionState.Degraded;

        ConnectionTooltip = string.Join(Environment.NewLine,
            states.OrderBy(kv => kv.Key).Select(kv => FormatProviderLine(kv.Key, kv.Value)));
    }

    private static string FormatProviderLine(MetricsProvider provider, ProviderHealth health)
    {
        var name = ProviderDisplayName(provider);
        if (health.IsOnline)
            return $"{name}: online (last update {health.LastSuccess:HH:mm:ss})";
        if (health.OfflineSince is null)
            return $"{name}: connecting...";

        var reason = string.IsNullOrEmpty(health.LastError) ? string.Empty : $" ({health.LastError})";
        return $"{name}: offline since {health.OfflineSince:HH:mm:ss}{reason}";
    }

    private static string ProviderDisplayName(MetricsProvider provider) => provider switch
    {
        MetricsProvider.WindowsExporter => "windows_exporter",
        MetricsProvider.LibreHardwareMonitor => "LibreHardwareMonitor",
        _ => provider.ToString(),
    };

    public event Action? SnapshotApplied;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
