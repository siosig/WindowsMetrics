using WindowsMetrics.Models.Display;

namespace WindowsMetrics.Models.Trends;

/// <summary>Which group(s) of trend series a call to <see cref="MetricTrends.Record"/> advanced.</summary>
[Flags]
public enum TrendGroups
{
    None = 0,
    Usage = 1,
    BoardTemperature = 2,
}

/// <summary>
/// Owns every trend series shown on screen and decides, from each incoming snapshot's
/// <see cref="SnapshotOrigin"/>, whether that snapshot should advance any of them. UI-agnostic
/// and independent of any dispatcher, so it can be unit tested directly. See
/// specs/005-ui-section-restructure/contracts/trend-graph.md "MetricTrends.Record" for the
/// exact recording rules this implements.
/// </summary>
public sealed class MetricTrends
{
    public const int Capacity = 30;

    public TrendSeriesBuffer CpuUsage { get; } = new(Capacity);
    public TrendSeriesBuffer MemoryUsage { get; } = new(Capacity);
    public TrendSeriesBuffer GpuUsage { get; } = new(Capacity);

    public TrendSeriesBuffer BoardSys { get; } = new(Capacity);
    public TrendSeriesBuffer BoardCpu { get; } = new(Capacity);
    public TrendSeriesBuffer BoardMos { get; } = new(Capacity);
    public TrendSeriesBuffer BoardPch { get; } = new(Capacity);
    public TrendSeriesBuffer BoardSock { get; } = new(Capacity);
    public TrendSeriesBuffer BoardPcie { get; } = new(Capacity);
    public TrendSeriesBuffer BoardM2 { get; } = new(Capacity);

    /// <summary>
    /// Records <paramref name="snapshot"/> into the series its <see cref="MetricsSnapshot.Origin"/>
    /// is allowed to advance. A snapshot with no origin, or one whose scrape failed, advances
    /// nothing -- this is what keeps a stalled provider's graphs frozen instead of repeating the
    /// last value (FR-017).
    /// </summary>
    public TrendGroups Record(MetricsSnapshot snapshot)
    {
        if (snapshot.Origin is not { } origin || !origin.Succeeded)
            return TrendGroups.None;

        return origin.Provider switch
        {
            MetricsProvider.WindowsExporter => RecordUsage(snapshot),
            MetricsProvider.LibreHardwareMonitor => RecordBoardTemperature(snapshot),
            _ => TrendGroups.None,
        };
    }

    private TrendGroups RecordUsage(MetricsSnapshot snapshot)
    {
        bool any = false;
        if (snapshot.Cpu.IsUsageKnown) { CpuUsage.Push(snapshot.Cpu.TotalUsagePercent); any = true; }
        if (snapshot.Memory.IsUsageKnown) { MemoryUsage.Push(snapshot.Memory.UsagePercent); any = true; }
        if (snapshot.Gpu.IsUsageKnown) { GpuUsage.Push(snapshot.Gpu.OverallPercent); any = true; }
        return any ? TrendGroups.Usage : TrendGroups.None;
    }

    private TrendGroups RecordBoardTemperature(MetricsSnapshot snapshot)
    {
        var t = snapshot.Temperatures;
        BoardSys.Push(t.SysCelsius);
        BoardCpu.Push(t.BoardCpuCelsius);
        BoardMos.Push(t.MosCelsius);
        BoardPch.Push(t.PchCelsius);
        BoardSock.Push(t.SockCelsius);
        BoardPcie.Push(t.PcieCelsius);
        BoardM2.Push(t.M2Celsius);
        return TrendGroups.BoardTemperature;
    }
}
