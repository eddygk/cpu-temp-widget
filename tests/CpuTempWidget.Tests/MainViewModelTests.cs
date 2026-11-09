using CpuTempWidget.Services;
using CpuTempWidget.ViewModels;
using Xunit;

namespace CpuTempWidget.Tests;

public class MainViewModelTests
{
    private sealed class FakeSource : ITemperatureSource
    {
        public Queue<TemperatureReading> Readings { get; } = new();
        public bool Disposed { get; private set; }
        public TemperatureReading Read() => Readings.Count > 0 ? Readings.Dequeue() : TemperatureReading.Failure("no more fake data");
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeStore(Settings initial) : ISettingsStore
    {
        public Settings Current { get; private set; } = initial;
        public int Saves { get; private set; }
        public Settings Load() => Current;
        public void Save(Settings settings) { Current = settings; Saves++; }
    }

    private static (MainViewModel vm, FakeSource source, FakeStore store) Create(Settings? settings = null)
    {
        var source = new FakeSource();
        var store = new FakeStore(settings ?? new Settings());
        return (new MainViewModel(source, store), source, store);
    }

    [Fact]
    public async Task SuccessfulRead_PublishesTemperatureStatusAndHistory()
    {
        var (vm, source, _) = Create();
        source.Readings.Enqueue(TemperatureReading.Success(57.4));

        await vm.RefreshAsync();

        Assert.Equal(57.4, vm.CurrentTemp);
        Assert.Contains("57.4", vm.StatusText);
        Assert.Single(vm.Samples);
        Assert.Equal(57.4, vm.MinTemp);
        Assert.Equal(57.4, vm.MaxTemp);
    }

    [Fact]
    public async Task FailedRead_ClearsTemperature_ShowsReason_AndBreaksTheLine()
    {
        var (vm, source, _) = Create();
        source.Readings.Enqueue(TemperatureReading.Success(60));
        source.Readings.Enqueue(TemperatureReading.Failure("driver unavailable"));

        await vm.RefreshAsync();
        await vm.RefreshAsync();

        Assert.Null(vm.CurrentTemp);
        Assert.Equal("driver unavailable", vm.StatusText);
        Assert.Equal(2, vm.Samples.Count);
        Assert.False(vm.Samples[1].IsValid);
        Assert.Equal(60, vm.MaxTemp);   // earlier valid reading still counts
    }

    [Fact]
    public async Task MinMax_TrackTheWindow()
    {
        var (vm, source, _) = Create();
        foreach (double t in new[] { 55.0, 48.5, 71.2, 60.0 })
            source.Readings.Enqueue(TemperatureReading.Success(t));

        for (int i = 0; i < 4; i++) await vm.RefreshAsync();

        Assert.Equal(48.5, vm.MinTemp);
        Assert.Equal(71.2, vm.MaxTemp);
        Assert.Equal(60.0, vm.CurrentTemp);
    }

    [Theory]
    [InlineData(100, 1000)]   // AMD: no faster than once per second
    [InlineData(0, 1000)]
    [InlineData(2000, 2000)]
    [InlineData(5000, 5000)]
    public void SetInterval_NeverGoesBelowOneSecond_AndIsPersisted(int requested, int expected)
    {
        var (vm, _, store) = Create();

        vm.SetIntervalCommand.Execute(requested);

        Assert.Equal(expected, vm.IntervalMs);
        Assert.Equal(expected, store.Current.IntervalMs);
    }

    [Fact]
    public void StoredIntervalBelowTheFloor_IsClampedOnLoad()
    {
        var (vm, _, _) = Create(new Settings { IntervalMs = 250 });

        Assert.Equal(1000, vm.IntervalMs);
    }

    [Fact]
    public void RateFlags_FollowTheInterval()
    {
        var (vm, _, _) = Create();
        Assert.True(vm.IsRate1);

        vm.SetIntervalCommand.Execute(5000);

        Assert.False(vm.IsRate1);
        Assert.False(vm.IsRate2);
        Assert.True(vm.IsRate5);
    }

    [Fact]
    public void ToggleTopmost_FlipsAndPersists()
    {
        var (vm, _, store) = Create(new Settings { Topmost = true });

        vm.ToggleTopmostCommand.Execute(null);

        Assert.False(vm.Topmost);
        Assert.False(store.Current.Topmost);
    }

    [Fact]
    public void SavePosition_IsPersisted()
    {
        var (vm, _, store) = Create();

        vm.SavePosition(123, 45);

        Assert.Equal(123, store.Current.Left);
        Assert.Equal(45, store.Current.Top);
    }

    private sealed class ThrowOnceSource : ITemperatureSource
    {
        private int _calls;
        public TemperatureReading Read() =>
            Interlocked.Increment(ref _calls) == 1 ? throw new InvalidOperationException("sdk exploded") : TemperatureReading.Success(55);
        public void Dispose() { }
    }

    [Fact]
    public async Task PollingLoop_SurvivesAnExceptionInATick()
    {
        // Regression: the loop is fire-and-forget, so an escaping exception used to end polling silently.
        var vm = new MainViewModel(new ThrowOnceSource(), new FakeStore(new Settings()));

        vm.Start();
        // 1st tick throws (shown as "--" + reason), the 2nd (1 s later, the minimum interval) must still happen.
        bool recovered = false;
        for (int i = 0; i < 60 && !recovered; i++)
        {
            await Task.Delay(100);
            recovered = vm.CurrentTemp == 55;
        }
        vm.Dispose();

        Assert.True(recovered, "polling should continue after a failed tick");
    }

    [Fact]
    public async Task ExceptionInATick_ShowsNoStaleValue()
    {
        var (vm, source, _) = Create();
        source.Readings.Enqueue(TemperatureReading.Success(61));
        await vm.RefreshAsync();
        Assert.Equal(61, vm.CurrentTemp);

        source.Readings.Enqueue(TemperatureReading.Failure("AMD SDK not responding (read took over 5 s)"));
        await vm.RefreshAsync();

        Assert.Null(vm.CurrentTemp);         // "--", never the old 61 pretending to be live
        Assert.Contains("not responding", vm.StatusText);
    }

    [Fact]
    public void Dispose_ReleasesTheSource()
    {
        var (vm, source, _) = Create();

        vm.Dispose();

        Assert.True(source.Disposed);
    }
}
