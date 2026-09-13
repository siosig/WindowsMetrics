using System.Windows;

namespace WindowsMetrics.Models.Trends;

/// <summary>
/// Pure value-to-pixel conversion for <see cref="Views.Controls.TrendGraph"/>. Kept free of any
/// WPF rendering dependency (beyond <see cref="Point"/>) so the geometry math is unit-testable
/// without a UI thread.
/// </summary>
public static class TrendScale
{
    /// <summary>
    /// Maps <paramref name="value"/> onto the vertical axis, clamping to the
    /// [<paramref name="min"/>, <paramref name="max"/>] range first so an out-of-range reading
    /// pins to an edge instead of being drawn off-canvas.
    /// </summary>
    public static double ToY(double value, double min, double max, double height)
    {
        if (max <= min)
            throw new ArgumentOutOfRangeException(nameof(max), max, "max must be greater than min.");
        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "height must not be negative.");

        double clamped = Math.Clamp(value, min, max);
        return height - (clamped - min) / (max - min) * height;
    }

    /// <summary>
    /// Splits <paramref name="values"/> (oldest-first) into drawable polyline runs, breaking at
    /// NaN gaps. A run of fewer than two points cannot form a line segment and is omitted.
    /// The most recent value is placed at the right edge, matching <see cref="SparklineBuffer"/>'s
    /// existing layout: the buffer's nominal <paramref name="capacity"/> (not just how many
    /// points have been collected so far) sets the horizontal step, so a partially-filled buffer
    /// still anchors its data to the right rather than stretching it across the full width.
    /// </summary>
    public static IReadOnlyList<Point[]> BuildRuns(
        double[] values, int capacity, double width, double height, double min, double max)
    {
        int n = values.Length;
        var runs = new List<Point[]>();
        if (n == 0) return runs;

        int total = Math.Max(n, capacity);
        double xStep = width / Math.Max(total - 1, 1);

        var current = new List<Point>();
        for (int i = 0; i < n; i++)
        {
            double v = values[i];
            if (double.IsNaN(v))
            {
                FlushRun(current, runs);
                continue;
            }
            double x = (total - n + i) * xStep;
            double y = ToY(v, min, max, height);
            current.Add(new Point(x, y));
        }
        FlushRun(current, runs);
        return runs;
    }

    private static void FlushRun(List<Point> current, List<Point[]> runs)
    {
        if (current.Count >= 2) runs.Add([.. current]);
        current.Clear();
    }
}
