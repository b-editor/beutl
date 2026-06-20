using System.Numerics;

using Beutl.Media;

namespace Beutl.UnitTests.Engine;

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

    [Test]
    public void FromArgb_StoresComponents()
    {
        var c = Color.FromArgb(0x12, 0x34, 0x56, 0x78);
        Assert.That(c.A, Is.EqualTo(0x12));
        Assert.That(c.R, Is.EqualTo(0x34));
        Assert.That(c.G, Is.EqualTo(0x56));
        Assert.That(c.B, Is.EqualTo(0x78));
    }

    [Test]
    public void FromRgb_DefaultsAlphaToFull()
    {
        var c = Color.FromRgb(1, 2, 3);
        Assert.That(c.A, Is.EqualTo(0xff));
        Assert.That(c.R, Is.EqualTo(1));
    }

    [Test]
    public void FromUInt32_RoundTripsToUInt32()
    {
        uint value = 0x12345678;
        var color = Color.FromUInt32(value);
        Assert.That(color.ToUint32(), Is.EqualTo(value));
    }

    [Test]
    public void FromInt32_RoundTripsToInt32()
    {
        int value = 0x12345678;
        var color = Color.FromInt32(value);
        Assert.That(color.ToInt32(), Is.EqualTo(value));
    }

    [Test]
    public void Parse_KnownName()
    {
        var c = Color.Parse("Red");
        Assert.That(c.R, Is.EqualTo(0xff));
        Assert.That(c.G, Is.EqualTo(0));
        Assert.That(c.B, Is.EqualTo(0));
        Assert.That(c.A, Is.EqualTo(0xff));
    }

    [Test]
    public void Parse_KnownNameLooksUpExactName()
    {
        // KnownColors stores enum field names like "Red".
        Assert.That(Color.Parse("Red"), Is.EqualTo(Color.FromUInt32(0xffff0000)));
    }

    [Test]
    public void Parse_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => Color.Parse((string)null!));
    }

    [Test]
    public void Parse_UnknownThrows()
    {
        Assert.Throws<FormatException>(() => Color.Parse("NotAColor"));
    }

    [Test]
    public void TryParse_NullReturnsFalse()
    {
        Assert.That(Color.TryParse((string)null!, out _), Is.False);
    }

    [Test]
    public void TryParse_EmptyReturnsFalse()
    {
        Assert.That(Color.TryParse("", out _), Is.False);
        Assert.That(Color.TryParse(ReadOnlySpan<char>.Empty, out _), Is.False);
    }

    [Test]
    public void TryParse_InvalidName()
    {
        Assert.That(Color.TryParse("Foobar", out _), Is.False);
    }

    [Test]
    public void ToString_KnownReturnsName()
    {
        Assert.That(Color.Parse("Red").ToString(), Is.EqualTo("Red"));
    }

    [Test]
    public void ToString_UnknownReturnsHex()
    {
        var c = Color.FromArgb(0x12, 0x34, 0x56, 0x78);
        Assert.That(c.ToString(), Is.EqualTo("#12345678"));
    }

    [Test]
    public void Equals_AndHashCode()
    {
        var a = Color.FromArgb(255, 100, 100, 100);
        var b = Color.FromArgb(255, 100, 100, 100);
        var c = Color.FromArgb(255, 100, 100, 200);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"not a color"), Is.False);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void ToLinear_BlackIsZero()
    {
        var v = Color.FromArgb(255, 0, 0, 0).ToLinear();
        Assert.That(v.X, Is.EqualTo(0f).Within(1e-6));
        Assert.That(v.Y, Is.EqualTo(0f).Within(1e-6));
        Assert.That(v.Z, Is.EqualTo(0f).Within(1e-6));
        Assert.That(v.W, Is.EqualTo(1f).Within(1e-6));
    }

    [Test]
    public void ToLinear_WhiteIsOne()
    {
        var v = Color.FromArgb(255, 255, 255, 255).ToLinear();
        Assert.That(v.X, Is.EqualTo(1f).Within(1e-3));
        Assert.That(v.Y, Is.EqualTo(1f).Within(1e-3));
        Assert.That(v.Z, Is.EqualTo(1f).Within(1e-3));
        Assert.That(v.W, Is.EqualTo(1f).Within(1e-6));
    }

    [Test]
    public void ToLinearPremultiplied_AlphaScalesRgb()
    {
        var color = Color.FromArgb(127, 255, 255, 255);
        var v = color.ToLinearPremultiplied();
        var alpha = 127f / 255f;

        Assert.That(v.W, Is.EqualTo(alpha).Within(1e-6));
        Assert.That(v.X, Is.EqualTo(alpha).Within(1e-3));
    }

    [Test]
    public void FromLinear_RoundTrip()
    {
        var input = Color.FromArgb(255, 100, 150, 200);
        var linear = input.ToLinear();
        var result = Color.FromLinear(linear);

        Assert.That(result.R, Is.EqualTo(input.R).Within(1));
        Assert.That(result.G, Is.EqualTo(input.G).Within(1));
        Assert.That(result.B, Is.EqualTo(input.B).Within(1));
        Assert.That(result.A, Is.EqualTo(input.A));
    }

    [Test]
    public void FromLinear_ClampsOutOfRange()
    {
        var clamped = Color.FromLinear(new Vector4(2f, -1f, 0.5f, 2f));
        Assert.That(clamped.A, Is.EqualTo(255));
    }

    [Test]
    public void IParsable_Parse()
    {
        var color = ParseViaInterface<Color>("Red");
        Assert.That(color, Is.EqualTo(Color.Parse("Red")));
    }

    [Test]
    public void IParsable_TryParse()
    {
        Assert.That(TryParseViaInterface<Color>("Red", out var color), Is.True);
        Assert.That(color, Is.EqualTo(Color.Parse("Red")));

        Assert.That(TryParseViaInterface<Color>(null, out _), Is.False);
    }

    [Test]
    public void ISpanParsable_Parse()
    {
        var color = SpanParseViaInterface<Color>("Red".AsSpan());
        Assert.That(color, Is.EqualTo(Color.Parse("Red")));
    }

    private static T ParseViaInterface<T>(string s) where T : IParsable<T>
        => T.Parse(s, null);

    private static bool TryParseViaInterface<T>(string? s, out T? result) where T : IParsable<T>
        => T.TryParse(s, null, out result);

    private static T SpanParseViaInterface<T>(ReadOnlySpan<char> s) where T : ISpanParsable<T>
        => T.Parse(s, null);

    [Test]
    public void ToBrushExtension_CreatesBrush()
    {
        var brush = Color.Parse("Red").ToBrush();
        Assert.That(brush.Color.CurrentValue, Is.EqualTo(Color.Parse("Red")));
    }

    [Test]
    public void ToBrushResourceExtension_CreatesResource()
    {
        var res = Color.Parse("Red").ToBrushResource();
        Assert.That(res, Is.Not.Null);
    }

    [Test]
    public void Colors_WellKnownConstants()
    {
        Assert.That(Colors.Black.ToUint32(), Is.EqualTo(0xff000000));
        Assert.That(Colors.White.ToUint32(), Is.EqualTo(0xffffffff));
        Assert.That(Colors.Red.ToUint32(), Is.EqualTo(0xffff0000));
        Assert.That(Colors.Green.ToUint32(), Is.EqualTo(0xff008000));
        Assert.That(Colors.Blue.ToUint32(), Is.EqualTo(0xff0000ff));
    }
}
