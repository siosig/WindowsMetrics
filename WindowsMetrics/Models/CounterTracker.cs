namespace WindowsMetrics.Models;

/// <summary>
/// Computes the rate of change (units/sec) from a CUMULATIVE counter metric.
/// Returns null on the first call, or when the counter resets (negative delta).
/// </summary>
public class CounterTracker
{
    private double? _lastValue;
    private ulong _lastTimeNano;

    public double? UpdateAndGetRate(double newValue, ulong newTimeNano)
    {
        if (_lastValue is null || newTimeNano <= _lastTimeNano)
        {
            _lastValue = newValue;
            _lastTimeNano = newTimeNano;
            return null;
        }

        double delta = newValue - _lastValue.Value;
        double elapsedSec = (newTimeNano - _lastTimeNano) / 1_000_000_000.0;

        _lastValue = newValue;
        _lastTimeNano = newTimeNano;

        if (delta < 0 || elapsedSec <= 0) return null;
        return delta / elapsedSec;
    }

    public void Reset()
    {
        _lastValue = null;
        _lastTimeNano = 0;
    }
}
