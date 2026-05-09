using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class RectConverterTests
{
    private readonly RectConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<float, float, float, float>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelRect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelSize)), Is.True);
    }

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(float[])), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Tuple<float, float, float, float>)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelRect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelSize)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void ConvertTo_FloatArray_ReturnsXYWidthHeight()
    {
        var r = new Rect(1, 2, 3, 4);
        float[] result = (float[])_converter.ConvertTo(null, null, r, typeof(float[]))!;
        Assert.That(result, Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
    }

    [Test]
    public void ConvertTo_Point_UsesXY()
    {
        var r = new Rect(1.5f, 2.5f, 30, 40);
        Point p = (Point)_converter.ConvertTo(null, null, r, typeof(Point))!;
        Assert.That(p, Is.EqualTo(new Point(1.5f, 2.5f)));
    }

    [Test]
    public void ConvertTo_Size_UsesWidthHeight()
    {
        var r = new Rect(1, 2, 30, 40);
        Size s = (Size)_converter.ConvertTo(null, null, r, typeof(Size))!;
        Assert.That(s, Is.EqualTo(new Size(30, 40)));
    }

    [Test]
    public void ConvertTo_PixelPoint_TruncatesXY()
    {
        var r = new Rect(1.7f, 2.9f, 3, 4);
        PixelPoint p = (PixelPoint)_converter.ConvertTo(null, null, r, typeof(PixelPoint))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(1, 2)));
    }

    [Test]
    public void ConvertTo_PixelRect_TruncatesAllValues()
    {
        var r = new Rect(1.5f, 2.5f, 3.5f, 4.5f);
        PixelRect pr = (PixelRect)_converter.ConvertTo(null, null, r, typeof(PixelRect))!;
        Assert.That(pr, Is.EqualTo(new PixelRect(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertTo_PixelSize_TruncatesWidthHeight()
    {
        var r = new Rect(1, 2, 3.7f, 4.9f);
        PixelSize ps = (PixelSize)_converter.ConvertTo(null, null, r, typeof(PixelSize))!;
        Assert.That(ps, Is.EqualTo(new PixelSize(3, 4)));
    }

    [Test]
    public void ConvertFrom_FloatArray_ReturnsRect()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, new float[] { 1.5f, 2.5f, 3.5f, 4.5f })!;
        Assert.That(r, Is.EqualTo(new Rect(1.5f, 2.5f, 3.5f, 4.5f)));
    }

    [Test]
    public void ConvertFrom_Tuple_ReturnsRect()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, new Tuple<float, float, float, float>(1, 2, 3, 4))!;
        Assert.That(r, Is.EqualTo(new Rect(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertFrom_Point_StartsWithZeroSize()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, new Point(2, 3))!;
        Assert.That(r, Is.EqualTo(new Rect(2, 3, 0, 0)));
    }

    [Test]
    public void ConvertFrom_Size_StartsAtOrigin()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, new Size(10, 20))!;
        Assert.That(r, Is.EqualTo(new Rect(0, 0, 10, 20)));
    }

    [Test]
    public void ConvertFrom_PixelPoint_StartsWithZeroSize()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, new PixelPoint(5, 6))!;
        Assert.That(r, Is.EqualTo(new Rect(5, 6, 0, 0)));
    }

    [Test]
    public void ConvertFrom_PixelRect_PreservesValues()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, new PixelRect(1, 2, 3, 4))!;
        Assert.That(r, Is.EqualTo(new Rect(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertFrom_PixelSize_StartsAtOrigin()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, new PixelSize(10, 20))!;
        Assert.That(r, Is.EqualTo(new Rect(0, 0, 10, 20)));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        Rect r = (Rect)_converter.ConvertFrom(null, null, "1,2,3,4")!;
        Assert.That(r, Is.EqualTo(new Rect(1, 2, 3, 4)));
    }
}
