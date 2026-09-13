using WindowsMetrics.Models;

namespace WindowsMetrics.Tests;

public class CounterTrackerTests
{
    [Fact]
    public void FirstCall_ReturnsNull()
    {
        var tracker = new CounterTracker();
        var result = tracker.UpdateAndGetRate(100, 1_000_000_000);
        Assert.Null(result);
    }

    [Fact]
    public void SecondCall_ReturnsCorrectRate()
    {
        var tracker = new CounterTracker();
        tracker.UpdateAndGetRate(0, 0);
        // 1000 bytes over 1 second
        var rate = tracker.UpdateAndGetRate(1000, 1_000_000_000);
        Assert.NotNull(rate);
        Assert.Equal(1000.0, rate!.Value, precision: 3);
    }

    [Fact]
    public void NegativeDelta_ReturnsNull()
    {
        var tracker = new CounterTracker();
        tracker.UpdateAndGetRate(1000, 1_000_000_000);
        var rate = tracker.UpdateAndGetRate(500, 2_000_000_000);
        Assert.Null(rate);
    }

    [Fact]
    public void SameTimestamp_ReturnsNull()
    {
        var tracker = new CounterTracker();
        tracker.UpdateAndGetRate(0, 1_000_000_000);
        var rate = tracker.UpdateAndGetRate(100, 1_000_000_000);
        Assert.Null(rate);
    }

    [Fact]
    public void Reset_AllowsFirstCallToReturnNull()
    {
        var tracker = new CounterTracker();
        tracker.UpdateAndGetRate(0, 0);
        tracker.UpdateAndGetRate(1000, 1_000_000_000);
        tracker.Reset();
        var rate = tracker.UpdateAndGetRate(2000, 2_000_000_000);
        Assert.Null(rate);
    }
}
