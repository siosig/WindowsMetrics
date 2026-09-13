using WindowsMetrics.Models;

namespace WindowsMetrics.Tests;

public class SparklineBufferTests
{
    [Fact]
    public void EmptyBuffer_ToArrayReturnsEmpty()
    {
        var buf = new SparklineBuffer(15);
        Assert.Empty(buf.ToArray());
    }

    [Fact]
    public void Push_StoresValues()
    {
        var buf = new SparklineBuffer(5);
        buf.Push(1); buf.Push(2); buf.Push(3);
        var arr = buf.ToArray();
        Assert.Equal(3, arr.Length);
        Assert.Equal([1, 2, 3], arr);
    }

    [Fact]
    public void Push_OverCapacity_WrapsAround()
    {
        var buf = new SparklineBuffer(3);
        buf.Push(1); buf.Push(2); buf.Push(3); buf.Push(4);
        var arr = buf.ToArray();
        Assert.Equal(3, arr.Length);
        Assert.Equal([2, 3, 4], arr);
    }

    [Fact]
    public void Push_NaN_StoredAsZero()
    {
        var buf = new SparklineBuffer(5);
        buf.Push(double.NaN);
        Assert.Equal(0, buf.ToArray()[0]);
    }

    [Fact]
    public void ToPoints_ReturnsCorrectCount()
    {
        var buf = new SparklineBuffer(10);
        for (int i = 0; i < 5; i++) buf.Push(i);
        var pts = buf.ToPoints(100, 24);
        Assert.Equal(5, pts.Count);
    }

    [Fact]
    public void ToPoints_EmptyBuffer_ReturnsEmpty()
    {
        var buf = new SparklineBuffer(10);
        var pts = buf.ToPoints(100, 24);
        Assert.Empty(pts);
    }
}
