using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PointConverterTests
{
    private readonly PointConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<float, float>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelRect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelSize)), Is.True);
    }

    [Test]
    public void CanConvertTo_UnknownTarget_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(DateTime)), Is.False);
    }

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Tuple<float, float>)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelRect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelSize)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void ConvertTo_FloatArray_ReturnsXY()
    {
        var p = new Point(2.5f, -1.25f);
        object? result = _converter.ConvertTo(null, null, p, typeof(float[]));
        Assert.That(result, Is.EqualTo(new[] { 2.5f, -1.25f }));
    }

    [Test]
    public void ConvertTo_Tuple_ReturnsXY()
    {
        var p = new Point(3, 4);
        object? result = _converter.ConvertTo(null, null, p, typeof(Tuple<float, float>));
        Assert.That(result, Is.EqualTo(new Tuple<float, float>(3, 4)));
    }

    [Test]
    public void ConvertTo_Rect_PutsPointAtOrigin()
    {
        var p = new Point(5, 6);
        Rect r = (Rect)_converter.ConvertTo(null, null, p, typeof(Rect))!;
        Assert.That(r.X, Is.EqualTo(5));
        Assert.That(r.Y, Is.EqualTo(6));
        Assert.That(r.Width, Is.EqualTo(0));
        Assert.That(r.Height, Is.EqualTo(0));
    }

    [Test]
    public void ConvertTo_PixelPoint_TruncatesCoordinates()
    {
        var p = new Point(1.9f, 2.7f);
        PixelPoint pp = (PixelPoint)_converter.ConvertTo(null, null, p, typeof(PixelPoint))!;
        Assert.That(pp.X, Is.EqualTo(1));
        Assert.That(pp.Y, Is.EqualTo(2));
    }

    [Test]
    public void ConvertFrom_FloatArray_ReturnsPoint()
    {
        Point p = (Point)_converter.ConvertFrom(null, null, new float[] { 1.5f, -2.0f })!;
        Assert.That(p.X, Is.EqualTo(1.5f));
        Assert.That(p.Y, Is.EqualTo(-2.0f));
    }

    [Test]
    public void ConvertFrom_PixelSize_ReturnsPoint()
    {
        Point p = (Point)_converter.ConvertFrom(null, null, new PixelSize(10, 20))!;
        Assert.That(p, Is.EqualTo(new Point(10, 20)));
    }

    [Test]
    public void ConvertFrom_String_RoundTripsViaParse()
    {
        Point parsed = (Point)_converter.ConvertFrom(null, null, "1.5,2.5")!;
        Assert.That(parsed, Is.EqualTo(new Point(1.5f, 2.5f)));
    }
}
