using System.Windows;
using System.Windows.Media;

namespace WindowsMetrics.Models;

public class BiDirSparklineBuffer
{
    private readonly double[] _pos; // Upward direction (RX)
    private readonly double[] _neg; // Downward direction (TX)
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

    // RX = above the center line, TX = below the center line. Returns two PointCollections.
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
