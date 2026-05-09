using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class PixelRectExtraTests
{
    [Test]
    public void Empty_IsAllZero()
    {
        Assert.That(PixelRect.Empty, Is.EqualTo(new PixelRect(0, 0, 0, 0)));
        Assert.That(PixelRect.Empty.IsEmpty, Is.True);
    }

    [Test]
    public void Constructor_FromSize_PositionAtOrigin()
    {
        var r = new PixelRect(new PixelSize(100, 200));
        Assert.That(r.Position, Is.EqualTo(PixelPoint.Origin));
        Assert.That(r.Size, Is.EqualTo(new PixelSize(100, 200)));
    }

    [Test]
    public void Constructor_FromPositionAndSize_AssignsAll()
    {
        var r = new PixelRect(new PixelPoint(5, 6), new PixelSize(10, 20));
        Assert.That(r.Right, Is.EqualTo(15));
        Assert.That(r.Bottom, Is.EqualTo(26));
    }

    [Test]
    public void Constructor_FromTwoCorners_CalculatesSize()
    {
        var r = new PixelRect(new PixelPoint(1, 2), new PixelPoint(11, 22));
        Assert.That(r.Width, Is.EqualTo(10));
        Assert.That(r.Height, Is.EqualTo(20));
    }

    [Test]
    public void Corners_ExposeExpectedPoints()
    {
        var r = new PixelRect(0, 0, 10, 20);
        Assert.That(r.TopLeft, Is.EqualTo(new PixelPoint(0, 0)));
        Assert.That(r.TopRight, Is.EqualTo(new PixelPoint(10, 0)));
        Assert.That(r.BottomLeft, Is.EqualTo(new PixelPoint(0, 20)));
        Assert.That(r.BottomRight, Is.EqualTo(new PixelPoint(10, 20)));
        Assert.That(r.Center, Is.EqualTo(new PixelPoint(5, 10)));
    }

    [Test]
    public void Equality_AndHashCode()
    {
        var a = new PixelRect(0, 0, 10, 20);
        var b = new PixelRect(0, 0, 10, 20);
        var c = new PixelRect(5, 0, 10, 20);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"x"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void Intersect_Disjoint_ReturnsEmpty()
    {
        var a = new PixelRect(0, 0, 10, 10);
        var b = new PixelRect(100, 100, 10, 10);
        Assert.That(a.Intersect(b), Is.EqualTo(PixelRect.Empty));
    }

    [Test]
    public void Union_HandlesEmptyOperands()
    {
        var a = new PixelRect(0, 0, 10, 10);
        Assert.That(PixelRect.Empty.Union(a), Is.EqualTo(a));
        Assert.That(a.Union(PixelRect.Empty), Is.EqualTo(a));
    }

    [Test]
    public void With_Methods_ReplaceFields()
    {
        var r = new PixelRect(1, 2, 3, 4);
        Assert.That(r.WithX(9), Is.EqualTo(new PixelRect(9, 2, 3, 4)));
        Assert.That(r.WithY(9), Is.EqualTo(new PixelRect(1, 9, 3, 4)));
        Assert.That(r.WithWidth(9), Is.EqualTo(new PixelRect(1, 2, 9, 4)));
        Assert.That(r.WithHeight(9), Is.EqualTo(new PixelRect(1, 2, 3, 9)));
    }

    [Test]
    public void ToRect_AndFromRect_RoundTripScale1()
    {
        var pr = new PixelRect(0, 0, 100, 50);
        Rect r = pr.ToRect(1f);
        Assert.That(r, Is.EqualTo(new Rect(0, 0, 100, 50)));
        Assert.That(PixelRect.FromRect(r), Is.EqualTo(pr));
    }

    [Test]
    public void ToRect_VectorScale_DividesEachAxis()
    {
        var pr = new PixelRect(0, 0, 100, 50);
        Rect r = pr.ToRect(new Vector(2f, 5f));
        Assert.That(r, Is.EqualTo(new Rect(0, 0, 50, 10)));
    }

    [Test]
    public void FromRect_WithScale_CeilsBottomRight()
    {
        var r = new Rect(0, 0, 1.5f, 2.5f);
        Assert.That(PixelRect.FromRect(r, 1f),
            Is.EqualTo(new PixelRect(0, 0, 2, 3)));
        Assert.That(PixelRect.FromRect(r, new Vector(2, 4)),
            Is.EqualTo(new PixelRect(0, 0, 3, 10)));
    }

    [Test]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.That(PixelRect.TryParse("garbage", out PixelRect r), Is.False);
        Assert.That(r, Is.EqualTo(default(PixelRect)));
    }

    [Test]
    public void ToString_UsesInvariantCulture()
    {
        Assert.That(new PixelRect(1, 2, 3, 4).ToString(), Is.EqualTo("1, 2, 3, 4"));
    }
}
