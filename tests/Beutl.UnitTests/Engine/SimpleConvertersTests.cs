using System.Numerics;
using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class RelativePointConverterTests
{
    private readonly RelativePointConverter _converter = new();

    [Test]
    public void CanConvertFrom_String_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_Other_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(int)), Is.False);
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var result = (RelativePoint)_converter.ConvertFrom(null, null, "0.5,0.5")!;
        Assert.That(result, Is.EqualTo(RelativePoint.Parse("0.5,0.5")));
    }
}

public class RelativeRectConverterTests
{
    private readonly RelativeRectConverter _converter = new();

    [Test]
    public void CanConvertFrom_String_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_Other_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(double)), Is.False);
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var result = (RelativeRect)_converter.ConvertFrom(null, null, "0%,0%,100%,100%")!;
        Assert.That(result, Is.EqualTo(RelativeRect.Parse("0%,0%,100%,100%")));
    }
}

public class FontFamilyConverterTests
{
    private readonly FontFamilyConverter _converter = new();

    [Test]
    public void CanConvertFrom_String_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_Other_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(int)), Is.False);
    }

    [Test]
    public void ConvertFrom_String_ReturnsFontFamily()
    {
        var family = (FontFamily)_converter.ConvertFrom(null, null, "Arial")!;
        Assert.That(family.Name, Is.EqualTo("Arial"));
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

public class ThicknessConverterTests
{
    private readonly ThicknessConverter _converter = new();

    [Test]
    public void CanConvertFrom_String_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_Other_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(float)), Is.False);
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var t = (Thickness)_converter.ConvertFrom(null, null, "1,2,3,4")!;
        Assert.That(t, Is.EqualTo(new Thickness(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertFrom_InvalidString_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _converter.ConvertFrom(null, null, "invalid"));
    }

    [Test]
    public void ConvertFrom_UnsupportedType_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertFrom(null, null, 42));
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

public class ColorConverterTests
{
    private readonly ColorConverter _converter = new();

    [Test]
    public void CanConvertFrom_String_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_Other_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(int)), Is.False);
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        var color = (Color)_converter.ConvertFrom(null, null, "#FF0000")!;
        Assert.That(color, Is.EqualTo(Color.Parse("#FF0000")));
    }

    [Test]
    public void ConvertFrom_InvalidString_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => _converter.ConvertFrom(null, null, "invalid"));
    }

    [Test]
    public void ConvertFrom_UnsupportedType_ThrowsNotSupportedException()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertFrom(null, null, 42));
    }
}
