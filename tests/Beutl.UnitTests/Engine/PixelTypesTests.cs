using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelPointTests
{
    [Test]
    public void Origin_IsZeroZero()
    {
        Assert.That(PixelPoint.Origin, Is.EqualTo(new PixelPoint(0, 0)));
    }

    [Test]
    public void Equality_AndOperators()
    {
        var a = new PixelPoint(1, 2);
        var b = new PixelPoint(1, 2);
        var c = new PixelPoint(2, 1);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"x"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void AddSubtract_ProducePerComponent()
    {
        var a = new PixelPoint(1, 2);
        var b = new PixelPoint(3, 4);
        Assert.That(a + b, Is.EqualTo(new PixelPoint(4, 6)));
        Assert.That(b - a, Is.EqualTo(new PixelPoint(2, 2)));
    }

    [Test]
    public void WithX_WithY_ReplaceComponent()
    {
        var p = new PixelPoint(1, 2);
        Assert.That(p.WithX(7), Is.EqualTo(new PixelPoint(7, 2)));
        Assert.That(p.WithY(9), Is.EqualTo(new PixelPoint(1, 9)));
    }

    [Test]
    public void ToPoint_DividesByScale()
    {
        var p = new PixelPoint(10, 20);
        Assert.That(p.ToPoint(2f), Is.EqualTo(new Point(5, 10)));
        Assert.That(p.ToPoint(new Vector(2, 4)), Is.EqualTo(new Point(5, 5)));
    }

    [Test]
    public void FromPoint_TruncatesToInteger()
    {
        Assert.That(PixelPoint.FromPoint(new Point(1.7f, 2.9f)), Is.EqualTo(new PixelPoint(1, 2)));
        Assert.That(
            PixelPoint.FromPoint(new Point(1.5f, 2.5f), 2f),
            Is.EqualTo(new PixelPoint(3, 5))
        );
        Assert.That(
            PixelPoint.FromPoint(new Point(1.5f, 2.5f), new Vector(2, 4)),
            Is.EqualTo(new PixelPoint(3, 10))
        );
    }

    [Test]
    public void Parse_ReadsTwoInts()
    {
        Assert.That(PixelPoint.Parse("3,4"), Is.EqualTo(new PixelPoint(3, 4)));
        Assert.That(PixelPoint.Parse("3,4".AsSpan()), Is.EqualTo(new PixelPoint(3, 4)));
    }

    [Test]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.That(PixelPoint.TryParse("nope", out PixelPoint p), Is.False);
        Assert.That(p, Is.EqualTo(default(PixelPoint)));
    }

    [Test]
    public void ToString_UsesInvariantCulture()
    {
        Assert.That(new PixelPoint(3, 4).ToString(), Is.EqualTo("3, 4"));
    }
}

public class PixelSizeTests
{
    [Test]
    public void Empty_IsZeroZero()
    {
        Assert.That(PixelSize.Empty, Is.EqualTo(new PixelSize(0, 0)));
    }

    [Test]
    public void AspectRatio_DividesWidthByHeight()
    {
        Assert.That(new PixelSize(1920, 1080).AspectRatio, Is.EqualTo(1920f / 1080f).Within(1e-5f));
    }

    [Test]
    public void Equality_AndHashCode()
    {
        var a = new PixelSize(100, 50);
        var b = new PixelSize(100, 50);
        var c = new PixelSize(50, 100);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"x"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void WithWidth_WithHeight_ReplaceDimension()
    {
        var s = new PixelSize(100, 50);
        Assert.That(s.WithWidth(200), Is.EqualTo(new PixelSize(200, 50)));
        Assert.That(s.WithHeight(75), Is.EqualTo(new PixelSize(100, 75)));
    }

    [Test]
    public void ToSize_DividesByScale()
    {
        var s = new PixelSize(100, 50);
        Assert.That(s.ToSize(2f), Is.EqualTo(new Size(50, 25)));
        Assert.That(s.ToSize(new Vector(2, 5)), Is.EqualTo(new Size(50, 10)));
    }

    [Test]
    public void FromSize_CeilsValues()
    {
        Assert.That(PixelSize.FromSize(new Size(1.1f, 2.2f), 1f), Is.EqualTo(new PixelSize(2, 3)));
        Assert.That(
            PixelSize.FromSize(new Size(1f, 2f), new Vector(2, 3)),
            Is.EqualTo(new PixelSize(2, 6))
        );
    }

    [Test]
    public void Parse_ReadsTwoInts()
    {
        Assert.That(PixelSize.Parse("100,50"), Is.EqualTo(new PixelSize(100, 50)));
    }

    [Test]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.That(PixelSize.TryParse("nope", out PixelSize s), Is.False);
        Assert.That(s, Is.EqualTo(default(PixelSize)));
    }

    [Test]
    public void ToString_UsesInvariantCulture()
    {
        Assert.That(new PixelSize(100, 50).ToString(), Is.EqualTo("100, 50"));
    }
}
