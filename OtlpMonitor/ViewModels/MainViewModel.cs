using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using OtlpMonitor.Models;
using OtlpMonitor.Models.Display;
using OtlpMonitor.Services;

namespace OtlpMonitor.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly MetricsChannel _channel;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cts;

    // Sparkline buffers
    public SparklineBuffer CpuSparkline { get; } = new(30);
    public SparklineBuffer NetworkSparkline { get; } = new(30);

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

    // --- System Objects ---
    private long _procCount, _threadCount, _handleCount;
    private double? _queueLenProc;
    public long ProcCount { get => _procCount; private set => Set(ref _procCount, value); }
    public long ThreadCount { get => _threadCount; private set => Set(ref _threadCount, value); }
    public long HandleCount { get => _handleCount; private set => Set(ref _handleCount, value); }
    public double? QueueLenProc { get => _queueLenProc; private set => Set(ref _queueLenProc, value); }

    // --- GPU ---
    private double _gpuOverall;
    public double GpuOverall { get => _gpuOverall; private set => Set(ref _gpuOverall, value); }

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

    // --- Status ---
    private bool _isOnline;
    public bool IsOnline { get => _isOnline; private set => Set(ref _isOnline, value); }

    public MainViewModel(MetricsChannel channel, Dispatcher dispatcher)
    {
        _channel = channel;
        _dispatcher = dispatcher;
    }

    public void StartConsuming(CancellationToken externalToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _ = ConsumeLoopAsync(_cts.Token);
        _ = OfflineWatcherAsync(_cts.Token);
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

    private async Task OfflineWatcherAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
    }

    private void ApplySnapshot(MetricsSnapshot s)
    {
        IsOnline = true;
        MemUsedBytes = s.Memory.UsedBytes;
        MemTotalBytes = s.Memory.TotalBytes;
        MemUsagePercent = s.Memory.UsagePercent;
        PagefileTotalBytes = s.Memory.PagefileTotalBytes;
        PagefileUsedBytes = s.Memory.PagefileUsedBytes;
        PagefilePercent = s.Memory.PagefilePercent;
        MemIsWarning = s.Memory.IsWarning;

        CpuTemp = s.Cpu.TemperatureCelsius;
        CpuC1 = s.Cpu.C1Percent;
        CpuC2 = s.Cpu.C2Percent;
        CpuC3 = s.Cpu.C3Percent;
        CpuDpc = s.Cpu.DpcPercent;
        CpuIntr = s.Cpu.InterruptPercent;
        CpuTotal = s.Cpu.TotalUsagePercent;
        CpuSparkline.Push(s.Cpu.TotalUsagePercent);

        ProcCount = s.System.ProcessCount;
        ThreadCount = s.System.ThreadCount;
        HandleCount = s.System.HandleCount;
        QueueLenProc = s.System.QueueLenProc;

        GpuOverall = s.Gpu.OverallPercent;
        TopGpuProc = s.Gpu.Processes;
        Temperatures = s.Temperatures;
        Disks = s.Disks;

        NetSend = s.Network.TotalSendBytesPerSec;
        NetRecv = s.Network.TotalRecvBytesPerSec;
        NetInterface = s.Network.InterfaceName;
        NetworkSparkline.Push(s.Network.TotalSendBytesPerSec + s.Network.TotalRecvBytesPerSec);

        TopMemProc = s.TopMemoryProcesses;
        TopCpuProc = s.TopCpuProcesses;
        TopNetProc = s.TopNetworkProcesses;

        OnPropertyChanged(nameof(CpuSparkline));
        OnPropertyChanged(nameof(NetworkSparkline));
        SnapshotApplied?.Invoke();
    }

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
