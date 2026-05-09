using Beutl.Converters;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelRectConverterTests
{
    private readonly PixelRectConverter _converter = new();

    [Test]
    public void CanConvertTo_KnownTargets_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertTo(null, typeof(int[])), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Tuple<int, int, int, int>)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertTo(null, typeof(PixelSize)), Is.True);
    }

    [Test]
    public void CanConvertFrom_KnownSources_ReturnsTrue()
    {
        Assert.That(_converter.CanConvertFrom(null, typeof(Tuple<int, int, int, int>)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Point)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Rect)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(Size)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelPoint)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(PixelSize)), Is.True);
        Assert.That(_converter.CanConvertFrom(null, typeof(string)), Is.True);
    }

    [Test]
    public void ConvertTo_IntArray_ReturnsXYWidthHeight()
    {
        var r = new PixelRect(1, 2, 3, 4);
        int[] result = (int[])_converter.ConvertTo(null, null, r, typeof(int[]))!;
        Assert.That(result, Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void ConvertTo_Point_UsesXY()
    {
        var r = new PixelRect(1, 2, 30, 40);
        Point p = (Point)_converter.ConvertTo(null, null, r, typeof(Point))!;
        Assert.That(p, Is.EqualTo(new Point(1, 2)));
    }

    [Test]
    public void ConvertTo_Rect_PreservesValues()
    {
        var r = new PixelRect(1, 2, 3, 4);
        Rect rect = (Rect)_converter.ConvertTo(null, null, r, typeof(Rect))!;
        Assert.That(rect, Is.EqualTo(new Rect(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertTo_Size_UsesWidthHeight()
    {
        var r = new PixelRect(1, 2, 30, 40);
        Size s = (Size)_converter.ConvertTo(null, null, r, typeof(Size))!;
        Assert.That(s, Is.EqualTo(new Size(30, 40)));
    }

    [Test]
    public void ConvertTo_PixelPoint_UsesXY()
    {
        var r = new PixelRect(5, 6, 7, 8);
        PixelPoint p = (PixelPoint)_converter.ConvertTo(null, null, r, typeof(PixelPoint))!;
        Assert.That(p, Is.EqualTo(new PixelPoint(5, 6)));
    }

    [Test]
    public void ConvertTo_PixelSize_UsesWidthHeight()
    {
        var r = new PixelRect(5, 6, 7, 8);
        PixelSize s = (PixelSize)_converter.ConvertTo(null, null, r, typeof(PixelSize))!;
        Assert.That(s, Is.EqualTo(new PixelSize(7, 8)));
    }

    [Test]
    public void ConvertFrom_Tuple_ReturnsRect()
    {
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, new Tuple<int, int, int, int>(1, 2, 3, 4))!;
        Assert.That(r, Is.EqualTo(new PixelRect(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertFrom_Point_StartsWithZeroSize()
    {
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, new Point(2.5f, 3.5f))!;
        Assert.That(r, Is.EqualTo(new PixelRect(2, 3, 0, 0)));
    }

    [Test]
    public void ConvertFrom_Rect_TruncatesCoordinates()
    {
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, new Rect(1.5f, 2.5f, 3.5f, 4.5f))!;
        Assert.That(r, Is.EqualTo(new PixelRect(1, 2, 3, 4)));
    }

    [Test]
    public void ConvertFrom_Size_StartsAtOrigin()
    {
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, new Size(10, 20))!;
        Assert.That(r, Is.EqualTo(new PixelRect(0, 0, 10, 20)));
    }

    [Test]
    public void ConvertFrom_PixelPoint_StartsWithZeroSize()
    {
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, new PixelPoint(2, 3))!;
        Assert.That(r, Is.EqualTo(new PixelRect(2, 3, 0, 0)));
    }

    [Test]
    public void ConvertFrom_PixelSize_StartsAtOrigin()
    {
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, new PixelSize(10, 20))!;
        Assert.That(r, Is.EqualTo(new PixelRect(0, 0, 10, 20)));
    }

    [Test]
    public void ConvertFrom_String_UsesParse()
    {
        PixelRect r = (PixelRect)_converter.ConvertFrom(null, null, "1,2,3,4")!;
        Assert.That(r, Is.EqualTo(new PixelRect(1, 2, 3, 4)));
    }
}
