using System.Globalization;
using System.Text;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class PointTests
{
    [Test]
    public void Default_IsZero_AndNotInvalid()
    {
        var p = new Point();
        Assert.That(p.IsDefault, Is.True);
        Assert.That(p.IsInvalid, Is.False);
    }

    [Test]
    public void Invalid_HasNaNComponents()
    {
        Assert.That(Point.Invalid.IsInvalid, Is.True);
    }

    [Test]
    public void Constructor_StoresComponents()
    {
        var p = new Point(3, 4);
        Assert.That(p.X, Is.EqualTo(3));
        Assert.That(p.Y, Is.EqualTo(4));
        Assert.That(p.IsDefault, Is.False);
    }

    [Test]
    public void ImplicitToVector_PreservesComponents()
    {
        var p = new Point(2, 3);
        Vector v = p;
        Assert.That(v.X, Is.EqualTo(2));
        Assert.That(v.Y, Is.EqualTo(3));
    }

    [Test]
    public void Negate_FlipsComponents()
    {
        Assert.That(-new Point(1, -2), Is.EqualTo(new Point(-1, 2)));
    }

    [Test]
    public void Operators_AddSubtractMultiplyDivide()
    {
        var a = new Point(1, 2);
        var b = new Point(3, 4);
        var v = new Vector(5, 6);

        Assert.That(a + b, Is.EqualTo(new Point(4, 6)));
        Assert.That(a + v, Is.EqualTo(new Point(6, 8)));
        Assert.That(b - a, Is.EqualTo(new Point(2, 2)));
        Assert.That(b - v, Is.EqualTo(new Point(-2, -2)));
        Assert.That(a * 3f, Is.EqualTo(new Point(3, 6)));
        Assert.That(3f * a, Is.EqualTo(new Point(3, 6)));
        Assert.That(b / 2f, Is.EqualTo(new Point(1.5f, 2)));
    }

    [Test]
    public void Equality_AndHashCode()
    {
        var a = new Point(1, 2);
        var b = new Point(1, 2);
        var c = new Point(1, 3);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"foo"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void WithX_WithY_ReplaceComponent()
    {
        var p = new Point(1, 2);
        Assert.That(p.WithX(9), Is.EqualTo(new Point(9, 2)));
        Assert.That(p.WithY(7), Is.EqualTo(new Point(1, 7)));
    }

    [Test]
    public void Deconstruct_ReturnsComponents()
    {
        var (x, y) = new Point(10, 20);
        Assert.That(x, Is.EqualTo(10));
        Assert.That(y, Is.EqualTo(20));
    }

    [Test]
    public void Transform_ByTranslation_AppliesOffset()
    {
        var p = new Point(1, 2);
        Matrix m = Matrix.CreateTranslation(10, 20);
        Assert.That(p.Transform(m), Is.EqualTo(new Point(11, 22)));
        Assert.That(p * m, Is.EqualTo(new Point(11, 22)));
    }

    [Test]
    public void Parse_DefaultCulture()
    {
        Assert.That(Point.Parse("1,2"), Is.EqualTo(new Point(1, 2)));
        Assert.That(Point.Parse("1,2".AsSpan()), Is.EqualTo(new Point(1, 2)));
    }

    [Test]
    public void Parse_WithCulture_AcceptsAlternateSeparator()
    {
        var fr = CultureInfo.GetCultureInfo("fr");
        Assert.That(Point.Parse("1;2", fr), Is.EqualTo(new Point(1, 2)));
    }

    [Test]
    public void TryParse_ValidAndInvalid()
    {
        Assert.That(Point.TryParse("1,2", out Point ok), Is.True);
        Assert.That(ok, Is.EqualTo(new Point(1, 2)));

        Assert.That(Point.TryParse("garbage", out Point bad), Is.False);
        Assert.That(bad, Is.EqualTo(default(Point)));
    }

    [Test]
    public void Parse_Utf8_RoundTrip()
    {
        ReadOnlySpan<byte> source = "1.5,2.5"u8;
        Assert.That(Point.Parse(source), Is.EqualTo(new Point(1.5f, 2.5f)));

        Assert.That(Point.TryParse(source, out Point ok), Is.True);
        Assert.That(ok, Is.EqualTo(new Point(1.5f, 2.5f)));

        Assert.That(Point.TryParse("garbage"u8, null, out _), Is.False);
    }

    [Test]
    public void ToString_NoCulture_UsesInvariant()
    {
        Assert.That(new Point(1.5f, 2.5f).ToString(), Is.EqualTo("1.5, 2.5"));
    }

    [Test]
    public void TryFormat_Char_AndUtf8_ReturnSameContent()
    {
        var p = new Point(1.5f, 2.5f);
        Span<char> chars = stackalloc char[32];
        Span<byte> bytes = stackalloc byte[32];

        Assert.That(p.TryFormat(chars, out int cw), Is.True);
        Assert.That(p.TryFormat(bytes, out int bw), Is.True);

        Assert.That(chars[..cw].ToString(), Is.EqualTo(Encoding.UTF8.GetString(bytes[..bw])));
    }
}
