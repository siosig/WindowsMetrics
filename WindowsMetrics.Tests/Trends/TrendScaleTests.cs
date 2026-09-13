using System.Windows;
using WindowsMetrics.Models.Trends;

namespace WindowsMetrics.Tests.Trends;

public class TrendScaleTests
{
    [Theory]
    [InlineData(50, 0, 100, 24, 12)]
    [InlineData(0, 0, 100, 24, 24)]
    [InlineData(100, 0, 100, 24, 0)]
    [InlineData(130, 0, 100, 48, 0)]
    [InlineData(-5, 0, 100, 48, 48)]
    [InlineData(50, 0, 100, 48, 24)]
    public void ToY_MapsValueOntoAxis(double value, double min, double max, double height, double expected)
    {
        Assert.Equal(expected, TrendScale.ToY(value, min, max, height), precision: 6);
    }

    [Fact]
    public void ToY_MaxNotGreaterThanMin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrendScale.ToY(5, 10, 10, 24));
    }

    [Fact]
    public void ToY_NegativeHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrendScale.ToY(5, 0, 100, -1));
    }

    [Fact]
    public void BuildRuns_NoGaps_ProducesOneRunAnchoredRight()
    {
        var runs = TrendScale.BuildRuns([10, 20], capacity: 4, width: 30, height: 100, min: 0, max: 100);
        var run = Assert.Single(runs);
        Assert.Equal(new Point(20, 90), run[0]);
        Assert.Equal(new Point(30, 80), run[1]);
    }

    [Fact]
    public void BuildRuns_LeadingSinglePointBeforeGap_IsDropped()
    {
        var runs = TrendScale.BuildRuns([10, double.NaN, 20, 30], capacity: 4, width: 30, height: 100, min: 0, max: 100);
        var run = Assert.Single(runs);
        Assert.Equal(new Point(20, 80), run[0]);
        Assert.Equal(new Point(30, 70), run[1]);
    }

    [Fact]
    public void BuildRuns_GapInMiddle_ProducesTwoRuns()
    {
        var runs = TrendScale.BuildRuns([10, 20, double.NaN, 30, 40], capacity: 5, width: 40, height: 100, min: 0, max: 100);
        Assert.Equal(2, runs.Count);
        Assert.Equal(new Point(0, 90), runs[0][0]);
        Assert.Equal(new Point(10, 80), runs[0][1]);
        Assert.Equal(new Point(30, 70), runs[1][0]);
        Assert.Equal(new Point(40, 60), runs[1][1]);
    }

    [Fact]
    public void BuildRuns_EmptyInput_ProducesNoRuns()
    {
        var runs = TrendScale.BuildRuns([], capacity: 4, width: 30, height: 100, min: 0, max: 100);
        Assert.Empty(runs);
    }
}
