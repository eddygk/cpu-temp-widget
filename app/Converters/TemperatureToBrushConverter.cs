using System.Globalization;
using System.Windows.Data;
using CpuTempWidget.Models;

namespace CpuTempWidget.Converters;

/// <summary>Colours the big number from the shared band table; null (no reading) is dim grey.</summary>
[ValueConversion(typeof(double?), typeof(System.Windows.Media.Brush))]
public sealed class TemperatureToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double celsius ? TemperatureBands.BrushFor(TemperatureBands.BandOf(celsius)) : TemperatureBands.DimBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
