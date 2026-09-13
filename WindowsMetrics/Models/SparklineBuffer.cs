using System.Windows;
using System.Windows.Media;

namespace OtlpMonitor.Models;

public class BiDirSparklineBuffer
{
    private readonly double[] _pos; // 上方向（RX）
    private readonly double[] _neg; // 下方向（TX）
    private int _head, _count;
    public int Capacity => _pos.Length;

    public BiDirSparklineBuffer(int capacity)
    {
        _pos = new double[capacity];
        _neg = new double[capacity];
    }

    public void Push(double pos, double neg)
    {
        _pos[_head] = Sanitize(pos);
        _neg[_head] = Sanitize(neg);
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    private static double Sanitize(double v) =>
        double.IsNaN(v) || double.IsInfinity(v) ? 0 : v;

    private (double[] Pos, double[] Neg) ToArrays()
    {
        var p = new double[_count];
        var n = new double[_count];
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - _count + i + Capacity) % Capacity;
            p[i] = _pos[idx];
            n[i] = _neg[idx];
        }
        return (p, n);
    }

    // RX=中心から上、TX=中心から下。2本のPointCollectionを返す。
    public (PointCollection Pos, PointCollection Neg) ToPoints(double width, double height)
    {
        var posPts = new PointCollection();
        var negPts = new PointCollection();
        if (_count == 0) return (posPts, negPts);

        var (posVals, negVals) = ToArrays();
        double halfH = height / 2.0;
        double max = Math.Max(posVals.Max(), negVals.Max());
        if (max <= 0) max = 1;

        int total = Math.Max(_count, Capacity);
        double xStep = width / Math.Max(total - 1, 1);

        for (int i = 0; i < _count; i++)
        {
            double x = (total - _count + i) * xStep;
            posPts.Add(new Point(x, halfH - posVals[i] / max * halfH));
            negPts.Add(new Point(x, halfH + negVals[i] / max * halfH));
        }
        return (posPts, negPts);
    }
}

public class SparklineBuffer
{
    private readonly double[] _buffer;
    private int _head;
    private int _count;

    public int Capacity => _buffer.Length;

    public SparklineBuffer(int capacity)
    {
        _buffer = new double[capacity];
        _head = 0;
        _count = 0;
    }

    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
        _buffer[_head] = value;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public double[] ToArray()
    {
        var result = new double[_count];
        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - _count + i + Capacity) % Capacity;
            result[i] = _buffer[idx];
        }
        return result;
    }

    public PointCollection ToPoints(double width, double height)
    {
        var points = new PointCollection();
        if (_count == 0) return points;

        var values = ToArray();
        double max = values.Max();
        if (max <= 0) max = 1;

        int total = Math.Max(_count, Capacity);
        double xStep = width / Math.Max(total - 1, 1);

        for (int i = 0; i < values.Length; i++)
        {
            double x = (total - values.Length + i) * xStep;
            double y = height - (values[i] / max * height);
            points.Add(new Point(x, y));
        }
        return points;
    }
}
