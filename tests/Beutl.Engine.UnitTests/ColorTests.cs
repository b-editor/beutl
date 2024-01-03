using Beutl.Media;

using NUnit.Framework;

namespace Beutl.Graphics.UnitTests;

public class ColorTests
{
    [Test]
    public void ParseRgbHash()
    {
        var result = Color.Parse("#ff8844");

        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0xff));
    }

    [Test]
    public void TryParseRgbHash()
    {
        var success = Color.TryParse("#ff8844", out Color result);

        Assert.That(success);
        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0xff));
    }

    [Test]
    public void ParseShortRgbHash()
    {
        var result = Color.Parse("#f84");

        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0xff));
    }

    [Test]
    public void TryParseShortRgbHash()
    {
        var success = Color.TryParse("#f84", out Color result);

        Assert.That(success);
        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0xff));
    }

    [Test]
    public void ParseArgbHash()
    {
        var result = Color.Parse("#40ff8844");

        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0x40));
    }

    [Test]
    public void TryParseArgbHash()
    {
        var success = Color.TryParse("#40ff8844", out Color result);

        Assert.That(success);
        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0x40));
    }

    [Test]
    public void ParseShortArgbHash()
    {
        var result = Color.Parse("#4f84");

        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0x44));
    }

    [Test]
    public void TryParseShortArgbHash()
    {
        var success = Color.TryParse("#4f84", out Color result);

        Assert.That(success);
        Assert.That(result.R, Is.EqualTo(0xff));
        Assert.That(result.G, Is.EqualTo(0x88));
        Assert.That(result.B, Is.EqualTo(0x44));
        Assert.That(result.A, Is.EqualTo(0x44));
    }

    [Test]
    public void ParseTooFew()
    {
        Assert.Throws<FormatException>(() => Color.Parse("#ff"));
    }

    [Test]
    public void TryParseTooFew()
    {
        Assert.That(!Color.TryParse("#ff", out _));
    }

    [Test]
    public void ParseTooMany()
    {
        Assert.Throws<FormatException>(() => Color.Parse("#ff5555555"));
    }

    [Test]
    public void TryParseTooMany()
    {
        Assert.That(!Color.TryParse("#ff5555555", out _));
    }

    [Test]
    public void ParseInvalidNumber()
    {
        Assert.Throws<FormatException>(() => Color.Parse("#ff808g80"));
    }

    [Test]
    public void TryParseInvalidNumber()
    {
        Assert.That(!Color.TryParse("#ff808g80", out _));
    }

    // テストケースは適当です。
    // I chose test case randomly.
    [Test]
    [TestCase("Aqua")]
    [TestCase("DeepPink")]
    [TestCase("Blue")]
    [TestCase("Gold")]
    public void ToHsv(string color)
    {
        var expected = Color.Parse(color);
        var hsv = expected.ToHsv();
        var actual = hsv.ToColor();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    [TestCase("Aqua")]
    [TestCase("DeepPink")]
    [TestCase("Blue")]
    [TestCase("Gold")]
    public void ToCmyk(string color)
    {
        var expected = Color.Parse(color);
        var cmyk = expected.ToCmyk();
        var actual = cmyk.ToColor();

        Assert.That(actual, Is.EqualTo(expected));
    }
}
