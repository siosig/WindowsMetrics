namespace WindowsMetrics.Models;

/// <summary>
/// A single flattened metric sample: one metric name, its labels, a numeric value,
/// a batch-scoped timestamp, and whether it is a monotonically increasing counter.
/// This is the input boundary that <see cref="Services.MetricsParser"/> consumes;
/// producers (OTLP flattening, Prometheus text parsing) only need to construct this type.
/// </summary>
public sealed record FlatMetric(
    string Name,
    Dictionary<string, string> Labels,
    double Value,
    ulong TimeNano,
    bool IsCumulative);
