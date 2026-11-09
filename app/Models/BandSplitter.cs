namespace CpuTempWidget.Models;

/// <summary>A straight piece of the graph line that lies entirely inside one colour band.</summary>
public readonly record struct BandSegment(double X0, double T0, double X1, double T1, int Band);

/// <summary>
/// Splits a line between two readings wherever it crosses a colour threshold, so each piece can be drawn in
/// its own band colour with a sharp colour change exactly at 70 / 85 C. Pure math: no WPF types, unit-testable.
/// </summary>
public static class BandSplitter
{
    public static IEnumerable<BandSegment> Split(double x0, double t0, double x1, double t1)
    {
        var fractions = new List<double>(2);
        foreach (double threshold in TemperatureBands.Thresholds)
        {
            bool crosses = (t0 < threshold && t1 > threshold) || (t0 > threshold && t1 < threshold);
            if (crosses) fractions.Add((threshold - t0) / (t1 - t0));
        }
        fractions.Sort();
        fractions.Add(1.0);

        double previous = 0;
        foreach (double f in fractions)
        {
            double xa = x0 + (x1 - x0) * previous, ta = t0 + (t1 - t0) * previous;
            double xb = x0 + (x1 - x0) * f, tb = t0 + (t1 - t0) * f;
            yield return new BandSegment(xa, ta, xb, tb, TemperatureBands.BandOf((ta + tb) / 2));
            previous = f;
        }
    }
}
