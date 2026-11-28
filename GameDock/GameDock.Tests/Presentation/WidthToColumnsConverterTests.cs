using System.Globalization;
using GameDock.Presentation.Converters;

namespace GameDock.Tests.Presentation;

public class WidthToColumnsConverterTests
{
    private readonly WidthToColumnsConverter _converter = new();

    private object Convert(object? value) =>
        _converter.Convert(value, typeof(int), null, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0.0, 3)]
    [InlineData(-10.0, 3)]
    [InlineData(double.NaN, 3)]
    [InlineData(100.0, 1)]
    [InlineData(329.0, 1)]
    [InlineData(330.0, 1)]
    [InlineData(659.0, 1)]
    [InlineData(660.0, 2)]
    [InlineData(990.0, 3)]
    [InlineData(5000.0, 3)]
    [InlineData(double.PositiveInfinity, 3)]
    public void Convert_ReturnsColumnsThatFit(double width, int expected)
    {
        Assert.Equal(expected, Convert(width));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("800")]
    [InlineData(800)]
    public void Convert_NonDoubleValue_ReturnsMaxColumns(object? value)
    {
        Assert.Equal(3, Convert(value));
    }

    [Fact]
    public void Convert_HonoursCustomSettings()
    {
        _converter.MinCardWidth = 200;
        _converter.MaxColumns = 5;

        Assert.Equal(4, Convert(800.0));
        Assert.Equal(5, Convert(10_000.0));
        Assert.Equal(5, Convert(double.NaN));
    }

    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() =>
            _converter.ConvertBack(2, typeof(double), null, CultureInfo.InvariantCulture));
    }
}
