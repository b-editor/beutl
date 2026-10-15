using System.ComponentModel;
using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

// The converters that read their type from a string share one contract.
public class StringConverterTests
{
    public static IEnumerable<ConverterCase> Converters()
    {
        yield return new(new CornerRadiusConverter(), "1,2,3,4", new CornerRadius(1, 2, 3, 4), typeof(int));
        yield return new(new ParsableTypeConverter<PixelPoint>(), "12,34", new PixelPoint(12, 34), typeof(DateTime));
        yield return new(new ParsableTypeConverter<PixelRect>(), "1,2,3,4", new PixelRect(1, 2, 3, 4), typeof(DateTime));
        yield return new(new ParsableTypeConverter<PixelSize>(), "10,20", new PixelSize(10, 20), typeof(DateTime));
        yield return new(new ParsableTypeConverter<Point>(), "1.5,2.5", new Point(1.5f, 2.5f), typeof(DateTime));
        yield return new(new ParsableTypeConverter<Rect>(), "1,2,3,4", new Rect(1, 2, 3, 4), typeof(DateTime));
        yield return new(new ParsableTypeConverter<Size>(), "10,20", new Size(10, 20), typeof(DateTime));
        yield return new(new ParsableTypeConverter<Beutl.Graphics.Vector>(), "1.5,2.5", new Beutl.Graphics.Vector(1.5f, 2.5f), typeof(DateTime));
        yield return new(new ParsableTypeConverter<Thickness>(), "1,2,3,4", new Thickness(1, 2, 3, 4), typeof(float));
        yield return new(new ParsableTypeConverter<Color>(), "#FF0000", Color.Parse("#FF0000"), typeof(int));
        yield return new(new RelativePointConverter(), "0.5,0.5", RelativePoint.Parse("0.5,0.5"), typeof(int));
        yield return new(
            new ParsableTypeConverter<RelativeRect>(),
            "0%,0%,100%,100%",
            RelativeRect.Parse("0%,0%,100%,100%"),
            typeof(double));
        yield return new(new FontFamilyConverter(), "Arial", new FontFamily("Arial"), typeof(int));
    }

    // Any string is a font family name, so FontFamilyConverter has no invalid input.
    public static IEnumerable<ConverterCase> ParsingConverters()
        => Converters().Where(static c => c.Converter is not FontFamilyConverter);

    [TestCaseSource(nameof(Converters))]
    public void CanConvertFrom_String_ReturnsTrue(ConverterCase c)
    {
        Assert.That(c.Converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [TestCaseSource(nameof(Converters))]
    public void CanConvertFrom_UnknownSource_ReturnsFalse(ConverterCase c)
    {
        Assert.That(c.Converter.CanConvertFrom(null, c.UnknownSource), Is.False);
    }

    [TestCaseSource(nameof(Converters))]
    public void ConvertFrom_String_UsesParse(ConverterCase c)
    {
        Assert.That(c.Converter.ConvertFrom(null, null, c.Valid), Is.EqualTo(c.Expected));
    }

    [TestCaseSource(nameof(ParsingConverters))]
    public void ConvertFrom_InvalidString_ThrowsFormatException(ConverterCase c)
    {
        Assert.Throws<FormatException>(() => c.Converter.ConvertFrom(null, null, "invalid"));
    }

    [TestCaseSource(nameof(Converters))]
    public void ConvertFrom_UnsupportedType_ThrowsNotSupportedException(ConverterCase c)
    {
        Assert.Throws<NotSupportedException>(() => c.Converter.ConvertFrom(null, null, 42));
    }

    public sealed record ConverterCase(TypeConverter Converter, string Valid, object Expected, Type UnknownSource)
    {
        public override string ToString() => $"{Converter.GetType().Name}({Expected.GetType().Name})";
    }
}

public class MatrixConverterTests
{
    private readonly ParsableTypeConverter<Matrix> _converter = new();

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var matrix = (Matrix)_converter.ConvertFrom(null, null, "none")!;
        Assert.That(matrix, Is.EqualTo(Matrix.Identity));
    }
}

public class GradingColorConverterTests
{
    private readonly ParsableTypeConverter<GradingColor> _converter = new();

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var c = (GradingColor)_converter.ConvertFrom(null, null, "0.1, 0.2, 0.3")!;
        Assert.That(c, Is.EqualTo(GradingColor.Parse("0.1, 0.2, 0.3")));
    }
}
