using System.Globalization;
using System.Windows.Data;

namespace GameDock.Presentation.Converters;

/// <summary>Number of card columns that fit into the given width (1..<see cref="MaxColumns"/>).</summary>
public sealed class WidthToColumnsConverter : IValueConverter
{
    /// <summary>Narrowest comfortable card width in device-independent pixels.</summary>
    public double MinCardWidth { get; set; } = 330;

    public int MaxColumns { get; set; } = 3;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width) || width <= 0)
            return MaxColumns;

        return Math.Clamp((int)(width / MinCardWidth), 1, MaxColumns);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
