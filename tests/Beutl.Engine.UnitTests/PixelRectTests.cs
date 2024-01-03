using Beutl.Media;

using NUnit.Framework;

namespace Beutl.Graphics.UnitTests;

public class PixelRectTests
{
    [Test]
    public void Parse()
    {
        const string str = "20,80,1900,1000";
        var rect = PixelRect.Parse(str);

        Assert.That(rect, Is.EqualTo(new PixelRect(20, 80, 1900, 1000)));
    }

    [Test]
    public void Contains()
    {
        var rect = new PixelRect(20, 80, 1900, 1000);

        Assert.That(rect.Contains(new PixelPoint(1899, 999)));

        Assert.That(rect.Contains(new PixelRect(30, 90, 1870, 990)));
    }

    [Test]
    public void CenterRect()
    {
        var rect = new PixelRect(0, 0, 1920, 1080);
        var center = new PixelRect(0, 0, 1280, 720);

        center = rect.CenterRect(center);

        Assert.That(rect.Contains(center));
    }

    [Test]
    public void Intersect()
    {
        var rect = new PixelRect(0, 0, 100, 100)
            .Intersect(new PixelRect(50, 50, 100, 100));

        Assert.That(rect, Is.EqualTo(new PixelRect(50, 50, 50, 50)));
    }

    [Test]
    public void Intersects()
    {
        var rect = new PixelRect(0, 0, 100, 100);

        Assert.That(rect.Intersects(new PixelRect(50, 50, 100, 100)));

        Assert.That(!rect.Intersects(new PixelRect(100, 100, 100, 100)));
    }

    [Test]
    public void Translate()
    {
        var rect = new PixelRect(0, 0, 100, 100);
        rect = rect.Translate(new PixelPoint(25, 25));

        Assert.That(rect, Is.EqualTo(new PixelRect(25, 25, 100, 100)));
    }

    [Test]
    public void Union()
    {
        var rect = new PixelRect(0, 0, 100, 100)
            .Union(new PixelRect(50, 50, 100, 100));

        Assert.That(rect, Is.EqualTo(new PixelRect(0, 0, 150, 150)));
    }
}
