using WindowsMetrics.Models.Trends;

namespace WindowsMetrics.Tests.Trends;

public class TrendSeriesBufferTests
{
    [Fact]
    public void Push_BelowCapacity_KeepsAllValuesInOrder()
    {
        var buf = new TrendSeriesBuffer(3);
        buf.Push(1);
        buf.Push(2);
        Assert.Equal([1, 2], buf.ToArray());
    }

    [Fact]
    public void Push_OverCapacity_WrapsAround()
    {
        var buf = new TrendSeriesBuffer(3);
        buf.Push(1); buf.Push(2); buf.Push(3); buf.Push(4);
        Assert.Equal([2, 3, 4], buf.ToArray());
    }

    [Fact]
    public void Push_Null_StoredAsGap()
    {
        var buf = new TrendSeriesBuffer(3);
        buf.Push(null);
        Assert.True(double.IsNaN(buf.ToArray()[0]));
    }

    [Fact]
    public void Push_PositiveInfinity_StoredAsGap()
    {
        var buf = new TrendSeriesBuffer(3);
        buf.Push(double.PositiveInfinity);
        Assert.True(double.IsNaN(buf.ToArray()[0]));
    }

    [Fact]
    public void Constructor_CapacityBelowTwo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TrendSeriesBuffer(1));
    }
}
