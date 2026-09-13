namespace WindowsMetrics.Models.Trends;

/// <summary>
/// A fixed-capacity ring buffer of doubles for a single trend line. Unlike
/// <see cref="SparklineBuffer"/>, a missing sample is preserved as a gap (NaN) rather than
/// silently dropped, so <see cref="Trends.TrendScale.BuildRuns"/> can break the line there
/// instead of interpolating across data the provider never reported.
/// </summary>
public sealed class TrendSeriesBuffer
{
    private readonly double[] _buffer;
    private int _head;

    /// <summary>Number of points this buffer holds at most.</summary>
    public int Capacity { get; }

    /// <summary>Number of points pushed so far, capped at <see cref="Capacity"/>.</summary>
    public int Count { get; private set; }

    public TrendSeriesBuffer(int capacity)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 2.");
        Capacity = capacity;
        _buffer = new double[capacity];
    }

    /// <summary>
    /// Advances the buffer by one point. A null, NaN, or infinite value is stored as a gap
    /// (NaN) rather than being coerced to zero, since zero is a valid reading for these series.
    /// </summary>
    public void Push(double? value)
    {
        double stored = value is double v && double.IsFinite(v) ? v : double.NaN;
        _buffer[_head] = stored;
        _head = (_head + 1) % Capacity;
        if (Count < Capacity) Count++;
    }

    /// <summary>Returns the stored points oldest-first. Gaps are NaN.</summary>
    public double[] ToArray()
    {
        var result = new double[Count];
        for (int i = 0; i < Count; i++)
        {
            int idx = (_head - Count + i + Capacity) % Capacity;
            result[i] = _buffer[idx];
        }
        return result;
    }
}
