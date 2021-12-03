using BEditorNext.Media;

using NUnit.Framework;

namespace BEditorNext.Graphics.UnitTests;

public class ColorTests
{
    [Test]
    public void ParseRgbHash()
    {
        var result = Color.Parse("#ff8844");

        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0xff, result.A);
    }

    [Test]
    public void TryParseRgbHash()
    {
        var success = Color.TryParse("#ff8844", out Color result);

        Assert.True(success);
        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0xff, result.A);
    }

    [Test]
    public void ParseShortRgbHash()
    {
        var result = Color.Parse("#f84");

        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0xff, result.A);
    }

    [Test]
    public void TryParseShortRgbHash()
    {
        var success = Color.TryParse("#f84", out Color result);

        Assert.True(success);
        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0xff, result.A);
    }

    [Test]
    public void ParseArgbHash()
    {
        var result = Color.Parse("#40ff8844");

        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0x40, result.A);
    }

    [Test]
    public void TryParseArgbHash()
    {
        var success = Color.TryParse("#40ff8844", out Color result);

        Assert.True(success);
        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0x40, result.A);
    }

    [Test]
    public void ParseShortArgbHash()
    {
        var result = Color.Parse("#4f84");

        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0x44, result.A);
    }

    [Test]
    public void TryParseShortArgbHash()
    {
        var success = Color.TryParse("#4f84", out Color result);

        Assert.True(success);
        Assert.AreEqual(0xff, result.R);
        Assert.AreEqual(0x88, result.G);
        Assert.AreEqual(0x44, result.B);
        Assert.AreEqual(0x44, result.A);
    }

    [Test]
    public void ParseTooFew()
    {
        Assert.Throws<FormatException>(() => Color.Parse("#ff"));
    }

    [Test]
    public void TryParseTooFew()
    {
        Assert.False(Color.TryParse("#ff", out _), "");
    }

    [Test]
    public void ParseTooMany()
    {
        Assert.Throws<FormatException>(() => Color.Parse("#ff5555555"));
    }

    [Test]
    public void TryParseTooMany()
    {
        Assert.False(Color.TryParse("#ff5555555", out _));
    }

    [Test]
    public void ParseInvalidNumber()
    {
        Assert.Throws<FormatException>(() => Color.Parse("#ff808g80"));
    }

    [Test]
    public void TryParseInvalidNumber()
    {
        Assert.False(Color.TryParse("#ff808g80", out _));
    }
}
