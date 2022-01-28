using NUnit.Framework;

namespace BeUtl.Graphics.UnitTests;

public class SizeTests
{
    [Test]
    public void Parse()
    {
        const string str = "1920,1080";
        var size = Size.Parse(str);

        Assert.AreEqual(new Size(1920, 1080), size);
    }
    
    [Test]
    public void ParseSpan()
    {
        const string str = "1920,1080";
        var size = Size.Parse(str.AsSpan());

        Assert.AreEqual(new Size(1920, 1080), size);
    }

    [Test]
    public void Deflate()
    {
        var size = new Size(1920, 1080);
        var thickness = new Thickness(10, 15);

        size = size.Deflate(thickness);

        Assert.AreEqual(new Size(1900, 1050), size);
    }

    [Test]
    public void Inflate()
    {
        var size = new Size(1920, 1080);
        var thickness = new Thickness(15, 10);

        size = size.Inflate(thickness);

        Assert.AreEqual(new Size(1950, 1100), size);
    }
}
