using System.Windows;
using System.Windows.Media;

namespace OtlpMonitor.Models;

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
