using System.Windows.Media;

namespace CpuTempWidget.Models;

/// <summary>
/// Single source of truth for the colour bands used by the big number and the chart.
/// The thresholds are fixed: warm from 70 C, hot from 85 C (desktop Ryzen CPUs typically throttle around 90-95 C).
/// Brushes are frozen: unfrozen Freezables cost change-tracking resources and can't be shared across threads.
/// </summary>
public static class TemperatureBands
{
    public const double Warm = 70;
    public const double Hot = 85;

    /// <summary>Thresholds in ascending order; band index = number of thresholds at or below the temperature.</summary>
    public static readonly double[] Thresholds = [Warm, Hot];

    public static readonly Brush NormalBrush = Frozen(Colors.White);
    public static readonly Brush WarmBrush = Frozen(Color.FromRgb(0xFF, 0xB3, 0x40));
    public static readonly Brush HotBrush = Frozen(Color.FromRgb(0xFF, 0x55, 0x55));
    public static readonly Brush DimBrush = Frozen(Color.FromRgb(0x88, 0x88, 0x88));

    /// <summary>0 = normal, 1 = warm, 2 = hot.</summary>
    public static int BandOf(double celsius) => celsius >= Hot ? 2 : celsius >= Warm ? 1 : 0;

    public static Brush BrushFor(int band) => band switch { 2 => HotBrush, 1 => WarmBrush, _ => NormalBrush };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
