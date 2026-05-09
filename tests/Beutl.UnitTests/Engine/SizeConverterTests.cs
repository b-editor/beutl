using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class SizeConverterTests
{
    private readonly SizeConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Vector)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelSize)), Is.True);
    }

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelSize)), Is.True);
    }

    [Test]
    public void ConvertTo_FloatArray_ReturnsWidthHeight()
    {
        var size = new Size(7, 9);
        float[] result = (float[])_converter.ConvertTo(null, null, size, typeof(float[]))!;
        Assert.That(result, Is.EqualTo(new[] { 7f, 9f }));
    }

    [Test]
    public void ConvertTo_Point_PreservesCoords()
    {
        var size = new Size(3, 4);
        Point p = (Point)_converter.ConvertTo(null, null, size, typeof(Point))!;
        Assert.That(p, Is.EqualTo(new Point(3, 4)));
    }

    [Test]
    public void ConvertTo_Rect_StartsAtOrigin()
    {
        var size = new Size(2, 5);
        Rect r = (Rect)_converter.ConvertTo(null, null, size, typeof(Rect))!;
        Assert.That(r.X, Is.EqualTo(0));
        Assert.That(r.Y, Is.EqualTo(0));
        Assert.That(r.Width, Is.EqualTo(2));
        Assert.That(r.Height, Is.EqualTo(5));
    }

    [Test]
    public void ConvertTo_PixelSize_TruncatesToInt()
    {
        var size = new Size(1.7f, 2.4f);
        PixelSize pixel = (PixelSize)_converter.ConvertTo(null, null, size, typeof(PixelSize))!;
        Assert.That(pixel.Width, Is.EqualTo(1));
        Assert.That(pixel.Height, Is.EqualTo(2));
    }

    [Test]
    public void ConvertFrom_Vector_PreservesXY()
    {
        Size s = (Size)_converter.ConvertFrom(null, null, new Vector(3, 4))!;
        Assert.That(s, Is.EqualTo(new Size(3, 4)));
    }

    [Test]
    public void ConvertFrom_FloatArray_ReturnsSize()
    {
        Size s = (Size)_converter.ConvertFrom(null, null, new float[] { 5.5f, 6.6f })!;
        Assert.That(s.Width, Is.EqualTo(5.5f));
        Assert.That(s.Height, Is.EqualTo(6.6f));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        Size s = (Size)_converter.ConvertFrom(null, null, "10,20")!;
        Assert.That(s, Is.EqualTo(new Size(10, 20)));
    }
}
