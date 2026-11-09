namespace CpuTempWidget;

/// <summary>Persisted user settings (plain data; file IO lives in <see cref="Services.JsonSettingsStore"/>).</summary>
public sealed class Settings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Topmost { get; set; } = true;

    /// <summary>Poll interval. AMD: GetCPUParameters no faster than once per second, so 1000 is the floor.</summary>
    public int IntervalMs { get; set; } = 1000;
}
