using System.Globalization;
using System.Text;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class RelativePointTests
{
    [Test]
    public void StaticMembers_AreExpectedRelativePoints()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RelativePoint.TopLeft.Point, Is.EqualTo(new Point(0, 0)));
            Assert.That(RelativePoint.TopLeft.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(RelativePoint.Center.Point, Is.EqualTo(new Point(0.5f, 0.5f)));
            Assert.That(RelativePoint.Center.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(RelativePoint.BottomRight.Point, Is.EqualTo(new Point(1, 1)));
            Assert.That(RelativePoint.BottomRight.Unit, Is.EqualTo(RelativeUnit.Relative));
        });
    }

    [Test]
    public void Constructor_FromXY_StoresComponents()
    {
        var p = new RelativePoint(0.25f, 0.75f, RelativeUnit.Relative);
        Assert.Multiple(() =>
        {
            Assert.That(p.Point, Is.EqualTo(new Point(0.25f, 0.75f)));
            Assert.That(p.Unit, Is.EqualTo(RelativeUnit.Relative));
        });
    }

    [Test]
    public void Equality_RespectsBothPointAndUnit()
    {
        var a = new RelativePoint(1, 2, RelativeUnit.Absolute);
        var b = new RelativePoint(1, 2, RelativeUnit.Absolute);
        var differentUnit = new RelativePoint(1, 2, RelativeUnit.Relative);
        var differentPoint = new RelativePoint(2, 2, RelativeUnit.Absolute);

        Assert.Multiple(() =>
        {
            Assert.That(a == b, Is.True);
            Assert.That(a != differentUnit, Is.True);
            Assert.That(a != differentPoint, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)"foo"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(differentUnit.GetHashCode()));
        });
    }

    [Test]
    public void ToPixels_Absolute_ReturnsRawPoint()
    {
        var p = new RelativePoint(10, 20, RelativeUnit.Absolute);
        Assert.That(p.ToPixels(new Size(200, 100)), Is.EqualTo(new Point(10, 20)));
    }

    [Test]
    public void ToPixels_Relative_ScalesBySize()
    {
        var p = new RelativePoint(0.5f, 0.25f, RelativeUnit.Relative);
        Assert.That(p.ToPixels(new Size(200, 400)), Is.EqualTo(new Point(100, 100)));
    }

    [Test]
    public void Parse_PercentageProducesRelativeUnit()
    {
        var rp = RelativePoint.Parse("50%, 25%");
        Assert.Multiple(() =>
        {
            Assert.That(rp.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(rp.Point.X, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(rp.Point.Y, Is.EqualTo(0.25f).Within(1e-6));
        });
    }

    [Test]
    public void Parse_WithoutPercentageProducesAbsoluteUnit()
    {
        var rp = RelativePoint.Parse("3,4");
        Assert.Multiple(() =>
        {
            Assert.That(rp.Unit, Is.EqualTo(RelativeUnit.Absolute));
            Assert.That(rp.Point, Is.EqualTo(new Point(3, 4)));
        });
    }

    [Test]
    public void Parse_MixedUnit_Throws()
    {
        Assert.Throws<FormatException>(() => RelativePoint.Parse("50%, 25"));
    }

    [Test]
    public void TryParse_ReturnsFalseOnGarbage()
    {
        Assert.That(RelativePoint.TryParse("nope", out RelativePoint result), Is.False);
        Assert.That(result, Is.EqualTo(default(RelativePoint)));
    }

    [Test]
    public void TryParse_ParsesValidString()
    {
        Assert.That(RelativePoint.TryParse("10,20", out RelativePoint result), Is.True);
        Assert.That(result, Is.EqualTo(new RelativePoint(10, 20, RelativeUnit.Absolute)));
    }

    [Test]
    public void Parse_Utf8_PercentAndAbsolute()
    {
        var relative = RelativePoint.Parse("50%, 25%"u8);
        var absolute = RelativePoint.Parse("3,4"u8);

        Assert.Multiple(() =>
        {
            Assert.That(relative.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(relative.Point.X, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(absolute.Unit, Is.EqualTo(RelativeUnit.Absolute));
            Assert.That(absolute.Point, Is.EqualTo(new Point(3, 4)));
        });
    }

    [Test]
    public void TryParse_Utf8_HandlesGarbage()
    {
        Assert.That(RelativePoint.TryParse("oops"u8, out RelativePoint result), Is.False);
        Assert.That(result, Is.EqualTo(default(RelativePoint)));

        Assert.That(RelativePoint.TryParse("1,2"u8, out RelativePoint ok), Is.True);
        Assert.That(ok, Is.EqualTo(new RelativePoint(1, 2, RelativeUnit.Absolute)));
    }

    [Test]
    public void ToString_FormatsAbsoluteAndRelative()
    {
        var absolute = new RelativePoint(1.5f, 2.5f, RelativeUnit.Absolute);
        var relative = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative);

        Assert.Multiple(() =>
        {
            Assert.That(absolute.ToString(), Is.EqualTo(new Point(1.5f, 2.5f).ToString()));
            Assert.That(relative.ToString(), Is.EqualTo("25%, 50%"));
        });
    }

    [Test]
    public void ToString_WithProvider_UsesProviderSeparator()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var absolute = new RelativePoint(1.5f, 2.5f, RelativeUnit.Absolute);
        var relative = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative);

        Assert.Multiple(() =>
        {
            Assert.That(absolute.ToString(fr), Does.Contain(";"));
            Assert.That(relative.ToString(fr), Does.Contain(";").And.Contain("%"));
            Assert.That(relative.ToString("g", fr), Is.EqualTo(relative.ToString(fr)));
        });
    }

    [Test]
    public void TryFormat_Char_FormatsBothUnits()
    {
        Span<char> buffer = stackalloc char[32];

        var absolute = new RelativePoint(1.5f, 2.5f, RelativeUnit.Absolute);
        Assert.That(absolute.TryFormat(buffer, out int absWritten), Is.True);
        Assert.That(buffer[..absWritten].ToString(), Is.EqualTo(absolute.ToString()));

        var relative = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative);
        Assert.That(relative.TryFormat(buffer, out int relWritten, default, CultureInfo.InvariantCulture), Is.True);
        Assert.That(buffer[..relWritten].ToString(), Is.EqualTo("25%, 50%"));
    }

    [Test]
    public void TryFormat_Utf8_MatchesCharOutput()
    {
        var relative = new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative);
        Span<char> chars = stackalloc char[32];
        Span<byte> bytes = stackalloc byte[32];

        Assert.That(relative.TryFormat(chars, out int cw, default, CultureInfo.InvariantCulture), Is.True);
        Assert.That(relative.TryFormat(bytes, out int bw, default, CultureInfo.InvariantCulture), Is.True);

        Assert.That(Encoding.UTF8.GetString(bytes[..bw]), Is.EqualTo(chars[..cw].ToString()));
    }

    [Test]
    public void TryParse_StringWithProvider_DelegatesToSpan()
    {
        Assert.That(RelativePoint.TryParse("10,20", CultureInfo.InvariantCulture, out RelativePoint result), Is.True);
        Assert.That(result, Is.EqualTo(new RelativePoint(10, 20, RelativeUnit.Absolute)));
    }
}
