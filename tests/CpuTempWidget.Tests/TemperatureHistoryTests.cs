using CpuTempWidget.Models;
using Xunit;

namespace CpuTempWidget.Tests;

public class TemperatureHistoryTests
{
    private static TemperatureSample At(double seconds, double celsius) =>
        new(TimeSpan.FromSeconds(seconds), celsius);

    [Fact]
    public void DropsSamplesOlderThanTheWindow()
    {
        var h = new TemperatureHistory(TimeSpan.FromMinutes(5));
        h.Add(At(0, 50));
        h.Add(At(10, 51));
        h.Add(At(301, 52));   // 301 s after the first sample: the first falls out, the second stays

        var snapshot = h.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(10, snapshot[0].At.TotalSeconds);
    }

    [Fact]
    public void KeepsASampleExactlyAtTheWindowEdge()
    {
        var h = new TemperatureHistory(TimeSpan.FromMinutes(5));
        h.Add(At(0, 50));
        h.Add(At(300, 51));

        Assert.Equal(2, h.Count);
    }

    [Fact]
    public void MinMax_IgnoreFailedReads()
    {
        var h = new TemperatureHistory(TimeSpan.FromMinutes(5));
        h.Add(At(0, 55));
        h.Add(At(1, double.NaN));
        h.Add(At(2, 48));
        h.Add(At(3, 62));

        Assert.Equal(48, h.Min);
        Assert.Equal(62, h.Max);
    }

    [Fact]
    public void MinMax_AreNullWithoutValidSamples()
    {
        var h = new TemperatureHistory(TimeSpan.FromMinutes(5));
        Assert.Null(h.Min);
        Assert.Null(h.Max);

        h.Add(At(0, double.NaN));
        Assert.Null(h.Min);
        Assert.Null(h.Max);
    }

    [Fact]
    public void Snapshot_IsANewArrayEachTime_SoBindingsSeeAChange()
    {
        var h = new TemperatureHistory(TimeSpan.FromMinutes(5));
        h.Add(At(0, 50));

        Assert.NotSame(h.Snapshot(), h.Snapshot());
    }

    [Fact]
    public void NonPositiveWindow_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TemperatureHistory(TimeSpan.Zero));
}
