using CpuTempWidget.Models;
using Xunit;

namespace CpuTempWidget.Tests;

public class BandSplitterTests
{
    [Fact]
    public void NoThresholdCrossed_ReturnsOneSegment()
    {
        var segs = BandSplitter.Split(0, 50, 10, 60).ToList();

        var s = Assert.Single(segs);
        Assert.Equal(0, s.Band);
        Assert.Equal((0, 50, 10, 60), (s.X0, s.T0, s.X1, s.T1));
    }

    [Fact]
    public void CrossingWarmThreshold_SplitsExactlyAt70()
    {
        var segs = BandSplitter.Split(0, 60, 10, 80).ToList();

        Assert.Equal(2, segs.Count);
        Assert.Equal(0, segs[0].Band);
        Assert.Equal(1, segs[1].Band);
        Assert.Equal(70, segs[0].T1, 6);
        Assert.Equal(70, segs[1].T0, 6);
        Assert.Equal(5, segs[0].X1, 6);          // halfway in time, as 70 is halfway between 60 and 80
        Assert.Equal(segs[0].X1, segs[1].X0, 9); // pieces connect
    }

    [Fact]
    public void RisingThroughBothThresholds_GivesThreeBands()
    {
        var segs = BandSplitter.Split(0, 60, 10, 90).ToList();

        Assert.Equal(new[] { 0, 1, 2 }, segs.Select(s => s.Band));
        Assert.Equal(70, segs[0].T1, 6);
        Assert.Equal(85, segs[1].T1, 6);
        Assert.Equal(60, segs[0].T0, 6);
        Assert.Equal(90, segs[2].T1, 6);
    }

    [Fact]
    public void FallingThroughBothThresholds_GivesBandsInReverse()
    {
        var segs = BandSplitter.Split(0, 90, 10, 60).ToList();

        Assert.Equal(new[] { 2, 1, 0 }, segs.Select(s => s.Band));
        Assert.Equal(85, segs[0].T1, 6);
        Assert.Equal(70, segs[1].T1, 6);
    }

    [Fact]
    public void ReachingAThresholdWithoutCrossingIt_IsNotSplit()
    {
        Assert.Single(BandSplitter.Split(0, 65, 10, 70));
        Assert.Single(BandSplitter.Split(0, 70, 10, 70));
    }

    [Theory]
    [InlineData(40, 0)]
    [InlineData(69.99, 0)]
    [InlineData(70, 1)]
    [InlineData(84.99, 1)]
    [InlineData(85, 2)]
    [InlineData(95, 2)]
    public void BandOf_UsesThresholds(double celsius, int expected) =>
        Assert.Equal(expected, TemperatureBands.BandOf(celsius));
}
