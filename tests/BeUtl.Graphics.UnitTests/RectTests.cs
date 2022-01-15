using NUnit.Framework;

namespace BeUtl.Graphics.UnitTests;

public class RectTests
{
    [Test]
    public void Parse()
    {
        const string str = "20,80,1900,1000";
        var rect = Rect.Parse(str);

        Assert.AreEqual(new Rect(20, 80, 1900, 1000), rect);
    }

    [Test]
    public void Contains()
    {
        var rect = new Rect(20, 80, 1900, 1000);

        Assert.IsTrue(rect.Contains(new Point(1899, 999)));

        Assert.IsTrue(rect.Contains(new Rect(30, 90, 1870, 990)));
    }

    [Test]
    public void CenterRect()
    {
        var rect = new Rect(0, 0, 1920, 1080);
        var center = new Rect(0, 0, 1280, 720);

        center = rect.CenterRect(center);

        Assert.IsTrue(rect.Contains(center));
    }

    [Test]
    public void Inflate()
    {
        var rect = new Rect(0, 0, 1900, 1000);
        var thickness = new Thickness(0, 0, 20, 80);

        rect = rect.Inflate(thickness);

        Assert.AreEqual(new Rect(0, 0, 1920, 1080), rect);
    }

    [Test]
    public void Deflate()
    {
        var rect = new Rect(0, 0, 1950, 1100);
        var thickness = new Thickness(0, 0, 30, 20);

        rect = rect.Deflate(thickness);

        Assert.AreEqual(new Rect(0, 0, 1920, 1080), rect);
    }

    [Test]
    public void Intersect()
    {
        var rect = new Rect(0, 0, 100, 100)
            .Intersect(new Rect(50, 50, 100, 100));

        Assert.AreEqual(new Rect(50, 50, 50, 50), rect);
    }

    [Test]
    public void Intersects()
    {
        var rect = new Rect(0, 0, 100, 100);

        Assert.IsTrue(rect.Intersects(new Rect(50, 50, 100, 100)));

        Assert.IsFalse(rect.Intersects(new Rect(100, 100, 100, 100)));
    }

    [Test]
    public void Transform()
    {
        var rect = new Rect(0, 0, 100, 100);
        rect = rect.TransformToAABB(System.Numerics.Matrix3x2.CreateScale(2, 2) * System.Numerics.Matrix3x2.CreateTranslation(10, 10));

        Assert.AreEqual(new Rect(10, 10, 200, 200), rect);
    }

    [Test]
    public void Translate()
    {
        var rect = new Rect(0, 0, 100, 100);
        rect = rect.Translate(new Vector(25, 25));

        Assert.AreEqual(new Rect(25, 25, 100, 100), rect);
    }

    [Test]
    public void Normalize()
    {
        var rect = new Rect(new Point(100, 100), new Point(0, 0))
            .Normalize();

        Assert.AreEqual(new Rect(0, 0, 100, 100), rect);

        rect = new Rect(
            float.NegativeInfinity, float.PositiveInfinity,
            float.PositiveInfinity, float.PositiveInfinity)
            .Normalize();

        Assert.AreEqual(Rect.Empty, rect);
    }

    [Test]
    public void Union()
    {
        var rect = new Rect(0, 0, 100, 100)
            .Union(new Rect(50, 50, 100, 100));

        Assert.AreEqual(new Rect(0, 0, 150, 150), rect);
    }
}
