using System.ComponentModel;
using System.Numerics;
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
        yield return new(new PixelPointConverter(), "12,34", new PixelPoint(12, 34), typeof(DateTime));
        yield return new(new PixelRectConverter(), "1,2,3,4", new PixelRect(1, 2, 3, 4), typeof(DateTime));
        yield return new(new PixelSizeConverter(), "10,20", new PixelSize(10, 20), typeof(DateTime));
        yield return new(new PointConverter(), "1.5,2.5", new Point(1.5f, 2.5f), typeof(DateTime));
        yield return new(new RectConverter(), "1,2,3,4", new Rect(1, 2, 3, 4), typeof(DateTime));
        yield return new(new SizeConverter(), "10,20", new Size(10, 20), typeof(DateTime));
        yield return new(new VectorConverter(), "1.5,2.5", new Beutl.Graphics.Vector(1.5f, 2.5f), typeof(DateTime));
        yield return new(new ThicknessConverter(), "1,2,3,4", new Thickness(1, 2, 3, 4), typeof(float));
        yield return new(new ColorConverter(), "#FF0000", Color.Parse("#FF0000"), typeof(int));
        yield return new(new RelativePointConverter(), "0.5,0.5", RelativePoint.Parse("0.5,0.5"), typeof(int));
        yield return new(
            new RelativeRectConverter(),
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
        public override string ToString() => Converter.GetType().Name;
    }
}

public class MatrixConverterTests
{
    private readonly MatrixConverter _converter = new();

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(float[])), Is.True);
    }

    [Test]
    public void ConvertFrom_FloatArray_Length6_ReturnsAffineMatrix()
    {
        var array = new float[] { 1, 2, 3, 4, 5, 6 };
        var matrix = (Matrix)_converter.ConvertFrom(null, null, array)!;
        Assert.That(matrix.M11, Is.EqualTo(1));
        Assert.That(matrix.M12, Is.EqualTo(2));
        Assert.That(matrix.M21, Is.EqualTo(3));
        Assert.That(matrix.M22, Is.EqualTo(4));
        Assert.That(matrix.M31, Is.EqualTo(5));
        Assert.That(matrix.M32, Is.EqualTo(6));
    }

    [Test]
    public void ConvertFrom_FloatArray_Length9_ReturnsFullMatrix()
    {
        var array = new float[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        var matrix = (Matrix)_converter.ConvertFrom(null, null, array)!;
        Assert.That(matrix.M11, Is.EqualTo(1));
        Assert.That(matrix.M22, Is.EqualTo(5));
        Assert.That(matrix.M33, Is.EqualTo(9));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var matrix = (Matrix)_converter.ConvertFrom(null, null, "none")!;
        Assert.That(matrix, Is.EqualTo(Matrix.Identity));
    }

    [Test]
    public void ConvertTo_FloatArray_Returns9Components()
    {
        var matrix = Matrix.Identity;
        var array = (float[])_converter.ConvertTo(null, null, matrix, typeof(float[]))!;
        Assert.That(array.Length, Is.EqualTo(9));
        Assert.That(array[0], Is.EqualTo(1));
        Assert.That(array[4], Is.EqualTo(1));
        Assert.That(array[8], Is.EqualTo(1));
    }
}

public class GradingColorConverterTests
{
    private readonly GradingColorConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<float, float, float>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Vector3)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Color)), Is.True);
    }

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Tuple<float, float, float>)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Vector3)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Color)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void ConvertTo_FloatArray_ReturnsRGB()
    {
        var c = new GradingColor(0.1f, 0.2f, 0.3f);
        var array = (float[])_converter.ConvertTo(null, null, c, typeof(float[]))!;
        Assert.That(array, Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f }));
    }

    [Test]
    public void ConvertTo_Tuple_ReturnsRGB()
    {
        var c = new GradingColor(0.1f, 0.2f, 0.3f);
        var tup = (Tuple<float, float, float>)_converter.ConvertTo(null, null, c, typeof(Tuple<float, float, float>))!;
        Assert.That(tup.Item1, Is.EqualTo(0.1f));
        Assert.That(tup.Item2, Is.EqualTo(0.2f));
        Assert.That(tup.Item3, Is.EqualTo(0.3f));
    }

    [Test]
    public void ConvertTo_Vector3_ReturnsXYZ()
    {
        var c = new GradingColor(0.1f, 0.2f, 0.3f);
        var v = (Vector3)_converter.ConvertTo(null, null, c, typeof(Vector3))!;
        Assert.That(v, Is.EqualTo(c.ToVector3()));
    }

    [Test]
    public void ConvertTo_Color_ReturnsColor()
    {
        var c = new GradingColor(0.1f, 0.2f, 0.3f);
        var color = (Color)_converter.ConvertTo(null, null, c, typeof(Color))!;
        Assert.That(color, Is.EqualTo(c.ToColor()));
    }

    [Test]
    public void ConvertFrom_FloatArray_ReturnsGradingColor()
    {
        var c = (GradingColor)_converter.ConvertFrom(null, null, new[] { 0.1f, 0.2f, 0.3f })!;
        Assert.That(c.R, Is.EqualTo(0.1f));
        Assert.That(c.G, Is.EqualTo(0.2f));
        Assert.That(c.B, Is.EqualTo(0.3f));
    }

    [Test]
    public void ConvertFrom_Tuple_ReturnsGradingColor()
    {
        var c = (GradingColor)_converter.ConvertFrom(null, null, new Tuple<float, float, float>(0.4f, 0.5f, 0.6f))!;
        Assert.That(c.R, Is.EqualTo(0.4f));
        Assert.That(c.G, Is.EqualTo(0.5f));
        Assert.That(c.B, Is.EqualTo(0.6f));
    }

    [Test]
    public void ConvertFrom_Vector3_ReturnsGradingColor()
    {
        var c = (GradingColor)_converter.ConvertFrom(null, null, new Vector3(0.4f, 0.5f, 0.6f))!;
        Assert.That(c, Is.EqualTo(GradingColor.FromVector3(new Vector3(0.4f, 0.5f, 0.6f))));
    }

    [Test]
    public void ConvertFrom_Color_ReturnsGradingColor()
    {
        var color = Color.FromArgb(255, 128, 64, 32);
        var c = (GradingColor)_converter.ConvertFrom(null, null, color)!;
        Assert.That(c, Is.EqualTo(GradingColor.FromColor(color)));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var c = (GradingColor)_converter.ConvertFrom(null, null, "0.1, 0.2, 0.3")!;
        Assert.That(c, Is.EqualTo(GradingColor.Parse("0.1, 0.2, 0.3")));
    }
}
