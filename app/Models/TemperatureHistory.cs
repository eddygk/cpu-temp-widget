namespace CpuTempWidget.Models;

/// <summary>
/// Time-windowed buffer of readings: samples older than <see cref="Window"/> (measured against the newest
/// sample) are dropped. About 300 points at 1 s, 60 at 5 s, so a plain queue is plenty.
/// </summary>
public sealed class TemperatureHistory
{
    private readonly Queue<TemperatureSample> _items = new();

    public TemperatureHistory(TimeSpan window)
    {
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        Window = window;
    }

    public TimeSpan Window { get; }

    public int Count => _items.Count;

    public void Add(TemperatureSample sample)
    {
        _items.Enqueue(sample);
        while (_items.Count > 0 && sample.At - _items.Peek().At > Window)
            _items.Dequeue();
    }

    /// <summary>A new array every call, so a property bound to it sees a reference change and re-renders.</summary>
    public IReadOnlyList<TemperatureSample> Snapshot() => _items.ToArray();

    public double? Min => Aggregate(min: true);

    public double? Max => Aggregate(min: false);

    private double? Aggregate(bool min)
    {
        double? result = null;
        foreach (var s in _items)
        {
            if (!s.IsValid) continue;
            if (result is null || (min ? s.Celsius < result : s.Celsius > result)) result = s.Celsius;
        }
        return result;
    }
}
