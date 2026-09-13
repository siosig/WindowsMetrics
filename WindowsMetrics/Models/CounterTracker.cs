namespace OtlpMonitor.Models;

/// <summary>
/// CUMULATIVE カウンターメトリクスの差分 (units/sec) を計算する。
/// 初回 or カウンターリセット (負差分) 時は null を返す。
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
