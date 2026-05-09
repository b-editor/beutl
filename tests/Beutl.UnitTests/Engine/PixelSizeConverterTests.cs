using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelSizeConverterTests
{
    private readonly PixelSizeConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(int[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<int, int>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Vector)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelRect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Size)), Is.True);
    }

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(int[])), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Tuple<int, int>)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Vector)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelRect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void ConvertTo_IntArray_ReturnsWidthHeight()
    {
        var s = new PixelSize(7, 9);
        int[] result = (int[])_converter.ConvertTo(null, null, s, typeof(int[]))!;
        Assert.That(result, Is.EqualTo(new[] { 7, 9 }));
    }

    [Test]
    public void ConvertTo_Point_PreservesWidthHeight()
    {
        var s = new PixelSize(3, 4);
        Point p = (Point)_converter.ConvertTo(null, null, s, typeof(Point))!;
        Assert.That(p, Is.EqualTo(new Point(3, 4)));
    }

    [Test]
    public void ConvertTo_Rect_StartsAtOrigin()
    {
        var s = new PixelSize(2, 5);
        Rect r = (Rect)_converter.ConvertTo(null, null, s, typeof(Rect))!;
        Assert.That(r, Is.EqualTo(new Rect(0, 0, 2, 5)));
    }

    [Test]
    public void ConvertTo_Size_PreservesWidthHeight()
    {
        var s = new PixelSize(3, 4);
        Size sz = (Size)_converter.ConvertTo(null, null, s, typeof(Size))!;
        Assert.That(sz, Is.EqualTo(new Size(3, 4)));
    }

    [Test]
    public void ConvertTo_Vector_PreservesXY()
    {
        var s = new PixelSize(11, 12);
        Vector v = (Vector)_converter.ConvertTo(null, null, s, typeof(Vector))!;
        Assert.That(v.X, Is.EqualTo(11));
        Assert.That(v.Y, Is.EqualTo(12));
    }

    [Test]
    public void ConvertTo_PixelPoint_PreservesXY()
    {
        var s = new PixelSize(2, 5);
        PixelPoint p = (PixelPoint)_converter.ConvertTo(null, null, s, typeof(PixelPoint))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(2, 5)));
    }

    [Test]
    public void ConvertTo_PixelRect_StartsAtOrigin()
    {
        var s = new PixelSize(2, 5);
        PixelRect r = (PixelRect)_converter.ConvertTo(null, null, s, typeof(PixelRect))!;
        Assert.That(r, Is.EqualTo(new PixelRect(0, 0, 2, 5)));
    }

    [Test]
    public void ConvertFrom_IntArray_ReturnsSize()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, new[] { 1, 2 })!;
        Assert.That(s, Is.EqualTo(new PixelSize(1, 2)));
    }

    [Test]
    public void ConvertFrom_Tuple_ReturnsSize()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, new Tuple<int, int>(8, 9))!;
        Assert.That(s, Is.EqualTo(new PixelSize(8, 9)));
    }

    [Test]
    public void ConvertFrom_Point_TruncatesCoordinates()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, new Point(1.9f, 2.7f))!;
        Assert.That(s, Is.EqualTo(new PixelSize(1, 2)));
    }

    [Test]
    public void ConvertFrom_Rect_UsesWidthHeight()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, new Rect(1, 2, 30.7f, 40.9f))!;
        Assert.That(s, Is.EqualTo(new PixelSize(30, 40)));
    }

    [Test]
    public void ConvertFrom_Size_TruncatesCoordinates()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, new Size(2.5f, 3.5f))!;
        Assert.That(s, Is.EqualTo(new PixelSize(2, 3)));
    }

    [Test]
    public void ConvertFrom_Vector_TruncatesCoordinates()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, new Vector(2.9f, 3.5f))!;
        Assert.That(s, Is.EqualTo(new PixelSize(2, 3)));
    }

    [Test]
    public void ConvertFrom_PixelPoint_PreservesXY()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, new PixelPoint(11, 12))!;
        Assert.That(s, Is.EqualTo(new PixelSize(11, 12)));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        PixelSize s = (PixelSize)_converter.ConvertFrom(null, null, "10,20")!;
        Assert.That(s, Is.EqualTo(new PixelSize(10, 20)));
    }
}
