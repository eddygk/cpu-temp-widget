namespace CpuTempWidget.Services;

/// <summary>Result of one read: either a temperature or a human-readable reason it isn't available.</summary>
public readonly record struct TemperatureReading(bool Ok, double Celsius, string? Error)
{
    public static TemperatureReading Success(double celsius) => new(true, celsius, null);
    public static TemperatureReading Failure(string error) => new(false, double.NaN, error);
}

/// <summary>
/// Where temperatures come from. The real one wraps the AMD SDK; tests use a fake.
/// <see cref="Read"/> may block briefly (SMU access), so callers run it off the UI thread.
/// </summary>
public interface ITemperatureSource : IDisposable
{
    TemperatureReading Read();
}
