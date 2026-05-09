using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelPointConverterTests
{
    private readonly PixelPointConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(int[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<int, int>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Size)), Is.True);
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
        Assert.That(_converter.CanConvertFrom(null, typeof(int[])), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Tuple<int, int>)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelRect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelSize)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void CanConvertFrom_UnknownSource_ReturnsFalse()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(DateTime)), Is.False);
    }

    [Test]
    public void ConvertTo_IntArray_ReturnsXY()
    {
        var p = new PixelPoint(3, 4);
        int[] result = (int[])_converter.ConvertTo(null, null, p, typeof(int[]))!;
        Assert.That(result, Is.EqualTo(new[] { 3, 4 }));
    }

    [Test]
    public void ConvertTo_Tuple_ReturnsXY()
    {
        var p = new PixelPoint(7, 8);
        var result = (Tuple<int, int>)_converter.ConvertTo(null, null, p, typeof(Tuple<int, int>))!;
        Assert.That(result, Is.EqualTo(new Tuple<int, int>(7, 8)));
    }

    [Test]
    public void ConvertTo_Point_PreservesXY()
    {
        var p = new PixelPoint(2, 5);
        Point pt = (Point)_converter.ConvertTo(null, null, p, typeof(Point))!;
        Assert.That(pt, Is.EqualTo(new Point(2, 5)));
    }

    [Test]
    public void ConvertTo_Rect_PutsAtOrigin()
    {
        var p = new PixelPoint(11, 22);
        Rect r = (Rect)_converter.ConvertTo(null, null, p, typeof(Rect))!;
        Assert.That(r.X, Is.EqualTo(11));
        Assert.That(r.Y, Is.EqualTo(22));
        Assert.That(r.Width, Is.EqualTo(0));
        Assert.That(r.Height, Is.EqualTo(0));
    }

    [Test]
    public void ConvertTo_Size_PreservesXY()
    {
        var p = new PixelPoint(3, 4);
        Size s = (Size)_converter.ConvertTo(null, null, p, typeof(Size))!;
        Assert.That(s, Is.EqualTo(new Size(3, 4)));
    }

    [Test]
    public void ConvertTo_PixelRect_StartsWithZeroSize()
    {
        var p = new PixelPoint(5, 6);
        PixelRect r = (PixelRect)_converter.ConvertTo(null, null, p, typeof(PixelRect))!;
        Assert.That(r, Is.EqualTo(new PixelRect(5, 6, 0, 0)));
    }

    [Test]
    public void ConvertTo_PixelSize_PreservesXY()
    {
        var p = new PixelPoint(3, 4);
        PixelSize s = (PixelSize)_converter.ConvertTo(null, null, p, typeof(PixelSize))!;
        Assert.That(s, Is.EqualTo(new PixelSize(3, 4)));
    }

    [Test]
    public void ConvertFrom_IntArray_ReturnsPixelPoint()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, new[] { 1, 2 })!;
        Assert.That(p, Is.EqualTo(new PixelPoint(1, 2)));
    }

    [Test]
    public void ConvertFrom_Tuple_ReturnsPixelPoint()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, new Tuple<int, int>(8, 9))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(8, 9)));
    }

    [Test]
    public void ConvertFrom_Point_TruncatesCoordinates()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, new Point(1.9f, 2.7f))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(1, 2)));
    }

    [Test]
    public void ConvertFrom_Rect_UsesXY()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, new Rect(3.2f, 4.8f, 10, 20))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(3, 4)));
    }

    [Test]
    public void ConvertFrom_Size_UsesWidthHeight()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, new Size(5, 6))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(5, 6)));
    }

    [Test]
    public void ConvertFrom_PixelRect_UsesXY()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, new PixelRect(7, 8, 10, 11))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(7, 8)));
    }

    [Test]
    public void ConvertFrom_PixelSize_UsesWidthHeight()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, new PixelSize(3, 4))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(3, 4)));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        PixelPoint p = (PixelPoint)_converter.ConvertFrom(null, null, "12,34")!;
        Assert.That(p, Is.EqualTo(new PixelPoint(12, 34)));
    }
}
