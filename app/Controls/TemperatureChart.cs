using System.Windows;
using System.Windows.Media;
using CpuTempWidget.Models;

namespace CpuTempWidget.Controls;

/// <summary>
/// Scrolling line chart of recent temperatures. Follows the "FrameworkElement model" for controls whose
/// look comes from custom rendering: inputs are dependency properties flagged AffectsRender, drawing is
/// done in OnRender with frozen pens/brushes and one StreamGeometry per colour band.
/// The right edge is the newest sample; the left edge is <see cref="Span"/> earlier.
/// </summary>
public sealed class TemperatureChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<TemperatureSample>), typeof(TemperatureChart),
        new FrameworkPropertyMetadata(Array.Empty<TemperatureSample>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpanProperty = DependencyProperty.Register(
        nameof(Span), typeof(TimeSpan), typeof(TemperatureChart),
        new FrameworkPropertyMetadata(TimeSpan.FromMinutes(5), FrameworkPropertyMetadataOptions.AffectsRender),
        value => (TimeSpan)value > TimeSpan.Zero);

    public IReadOnlyList<TemperatureSample> Samples
    {
        get => (IReadOnlyList<TemperatureSample>)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public TimeSpan Span
    {
        get => (TimeSpan)GetValue(SpanProperty);
        set => SetValue(SpanProperty, value);
    }

    private const double PadY = 3;      // keeps the line/dot off the top and bottom edges
    private const double PadRight = 4;  // room for the "now" dot
    private const double DotRadius = 2.5;

    // Frozen once: the render path allocates no Freezables except the per-frame geometries (frozen after build).
    private static readonly Pen[] LinePens = [MakeLinePen(0), MakeLinePen(1), MakeLinePen(2)];
    private static readonly Pen GuidePen = MakeGuidePen();
    private static readonly Pen BasePen = MakePen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), 1);

    public TemperatureChart()
    {
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;   // purely visual: clicks fall through to the window (drag, context menu)
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 8 || h < 8) return;

        dc.DrawLine(BasePen, new Point(0, Math.Floor(h - 1) + 0.5), new Point(w, Math.Floor(h - 1) + 0.5));

        IReadOnlyList<TemperatureSample> samples = Samples;
        if (samples.Count == 0) return;

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var s in samples)
        {
            if (!s.IsValid) continue;
            if (s.Celsius < min) min = s.Celsius;
            if (s.Celsius > max) max = s.Celsius;
        }
        if (double.IsInfinity(min)) return;   // nothing valid to plot

        // Y range: a little headroom around the data, snapped to 5 C, always tall enough to show the 70 C guide.
        double lo = Math.Floor((min - 3) / 5) * 5;
        double hi = Math.Ceiling(Math.Max(max + 3, 75) / 5) * 5;
        if (hi - lo < 20) lo = hi - 20;

        double plotW = w - PadRight;
        TimeSpan now = samples[^1].At;
        TimeSpan start = now - Span;

        double X(TimeSpan at) => (at - start).TotalSeconds / Span.TotalSeconds * plotW;
        double Y(double t) => (h - PadY) - (t - lo) / (hi - lo) * (h - 2 * PadY);

        foreach (double threshold in TemperatureBands.Thresholds)
        {
            if (threshold <= lo || threshold >= hi) continue;
            double gy = Math.Floor(Y(threshold)) + 0.5;
            dc.DrawLine(GuidePen, new Point(0, gy), new Point(w, gy));
        }

        // One geometry (open context) per band; a figure continues only while consecutive pieces connect.
        var geometries = new StreamGeometry[3];
        var contexts = new StreamGeometryContext[3];
        var lastEnd = new Point?[3];
        for (int b = 0; b < 3; b++)
        {
            geometries[b] = new StreamGeometry();
            contexts[b] = geometries[b].Open();
        }

        for (int i = 1; i < samples.Count; i++)
        {
            TemperatureSample a = samples[i - 1], c = samples[i];
            if (!a.IsValid || !c.IsValid) continue;   // a failed read breaks the line

            foreach (BandSegment seg in BandSplitter.Split(X(a.At), a.Celsius, X(c.At), c.Celsius))
            {
                var p0 = new Point(seg.X0, Y(seg.T0));
                var p1 = new Point(seg.X1, Y(seg.T1));
                if (lastEnd[seg.Band] is not Point previous || Math.Abs(previous.X - p0.X) > 0.01 || Math.Abs(previous.Y - p0.Y) > 0.01)
                    contexts[seg.Band].BeginFigure(p0, isFilled: false, isClosed: false);
                contexts[seg.Band].LineTo(p1, isStroked: true, isSmoothJoin: true);
                lastEnd[seg.Band] = p1;
            }
        }

        for (int b = 0; b < 3; b++)
        {
            contexts[b].Close();
            geometries[b].Freeze();
            dc.DrawGeometry(null, LinePens[b], geometries[b]);
        }

        TemperatureSample newest = samples[^1];
        if (newest.IsValid)
            dc.DrawEllipse(TemperatureBands.BrushFor(TemperatureBands.BandOf(newest.Celsius)), null,
                new Point(X(newest.At), Y(newest.Celsius)), DotRadius, DotRadius);
    }

    private static Pen MakeLinePen(int band)
    {
        var pen = new Pen(TemperatureBands.BrushFor(band), 1.75)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Pen MakePen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen MakeGuidePen()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        var pen = new Pen(brush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        pen.Freeze();
        return pen;
    }
}
