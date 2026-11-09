namespace CpuTempWidget.Models;

/// <summary>
/// One reading. <see cref="At"/> comes from a monotonic stopwatch (not wall-clock time) so clock changes
/// can't distort the graph. A failed read is stored as NaN, which breaks the line instead of faking data.
/// </summary>
public readonly record struct TemperatureSample(TimeSpan At, double Celsius)
{
    public bool IsValid => !double.IsNaN(Celsius);
}
