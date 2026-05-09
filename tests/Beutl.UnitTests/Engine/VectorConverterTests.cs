using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class VectorConverterTests
{
    private readonly VectorConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<float, float>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelSize)), Is.True);
    }

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Tuple<float, float>)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelSize)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void ConvertTo_FloatArray_ReturnsXY()
    {
        var v = new Vector(2.5f, -1.25f);
        float[] result = (float[])_converter.ConvertTo(null, null, v, typeof(float[]))!;
        Assert.That(result, Is.EqualTo(new[] { 2.5f, -1.25f }));
    }

    [Test]
    public void ConvertTo_Tuple_ReturnsXY()
    {
        var v = new Vector(3, 4);
        var result = (Tuple<float, float>)_converter.ConvertTo(null, null, v, typeof(Tuple<float, float>))!;
        Assert.That(result, Is.EqualTo(new Tuple<float, float>(3, 4)));
    }

    [Test]
    public void ConvertTo_Point_ReturnsXY()
    {
        var v = new Vector(3, 4);
        Point p = (Point)_converter.ConvertTo(null, null, v, typeof(Point))!;
        Assert.That(p, Is.EqualTo(new Point(3, 4)));
    }

    [Test]
    public void ConvertTo_Size_ReturnsXY()
    {
        var v = new Vector(3, 4);
        Size s = (Size)_converter.ConvertTo(null, null, v, typeof(Size))!;
        Assert.That(s, Is.EqualTo(new Size(3, 4)));
    }

    [Test]
    public void ConvertTo_PixelPoint_TruncatesXY()
    {
        var v = new Vector(1.9f, 2.7f);
        PixelPoint p = (PixelPoint)_converter.ConvertTo(null, null, v, typeof(PixelPoint))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(1, 2)));
    }

    [Test]
    public void ConvertTo_PixelSize_TruncatesXY()
    {
        var v = new Vector(1.9f, 2.7f);
        PixelSize p = (PixelSize)_converter.ConvertTo(null, null, v, typeof(PixelSize))!;
        Assert.That(p, Is.EqualTo(new PixelSize(1, 2)));
    }

    [Test]
    public void ConvertFrom_FloatArray_ReturnsVector()
    {
        Vector v = (Vector)_converter.ConvertFrom(null, null, new float[] { 1.5f, 2.5f })!;
        Assert.That(v.X, Is.EqualTo(1.5f));
        Assert.That(v.Y, Is.EqualTo(2.5f));
    }

    [Test]
    public void ConvertFrom_Tuple_ReturnsVector()
    {
        Vector v = (Vector)_converter.ConvertFrom(null, null, new Tuple<float, float>(7, 8))!;
        Assert.That(v.X, Is.EqualTo(7));
        Assert.That(v.Y, Is.EqualTo(8));
    }

    [Test]
    public void ConvertFrom_Point_ReturnsVector()
    {
        Vector v = (Vector)_converter.ConvertFrom(null, null, new Point(2.5f, 3.5f))!;
        Assert.That(v.X, Is.EqualTo(2.5f));
        Assert.That(v.Y, Is.EqualTo(3.5f));
    }

    [Test]
    public void ConvertFrom_Size_ReturnsVector()
    {
        Vector v = (Vector)_converter.ConvertFrom(null, null, new Size(4, 5))!;
        Assert.That(v.X, Is.EqualTo(4));
        Assert.That(v.Y, Is.EqualTo(5));
    }

    [Test]
    public void ConvertFrom_PixelPoint_ReturnsVector()
    {
        Vector v = (Vector)_converter.ConvertFrom(null, null, new PixelPoint(11, 12))!;
        Assert.That(v.X, Is.EqualTo(11));
        Assert.That(v.Y, Is.EqualTo(12));
    }

    [Test]
    public void ConvertFrom_PixelSize_ReturnsVector()
    {
        Vector v = (Vector)_converter.ConvertFrom(null, null, new PixelSize(11, 12))!;
        Assert.That(v.X, Is.EqualTo(11));
        Assert.That(v.Y, Is.EqualTo(12));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        Vector v = (Vector)_converter.ConvertFrom(null, null, "1.5,2.5")!;
        Assert.That(v.X, Is.EqualTo(1.5f));
        Assert.That(v.Y, Is.EqualTo(2.5f));
    }
}
