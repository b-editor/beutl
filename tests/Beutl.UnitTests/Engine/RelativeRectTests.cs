using System.Globalization;
using System.Text;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class RelativeRectTests
{
    [Test]
    public void Fill_IsRelativeFullRectangle()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RelativeRect.Fill.Rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            Assert.That(RelativeRect.Fill.Unit, Is.EqualTo(RelativeUnit.Relative));
        });
    }

    [Test]
    public void Constructors_AllProduceSameRect()
    {
        var rect = new Rect(1, 2, 3, 4);
        var fromComponents = new RelativeRect(1, 2, 3, 4, RelativeUnit.Absolute);
        var fromRect = new RelativeRect(rect, RelativeUnit.Absolute);
        var fromPositionSize = new RelativeRect(new Point(1, 2), new Size(3, 4), RelativeUnit.Absolute);
        var fromCorners = new RelativeRect(new Point(1, 2), new Point(4, 6), RelativeUnit.Absolute);
        var fromSizeOnly = new RelativeRect(new Size(3, 4), RelativeUnit.Absolute);

        Assert.Multiple(() =>
        {
            Assert.That(fromComponents, Is.EqualTo(fromRect));
            Assert.That(fromComponents, Is.EqualTo(fromPositionSize));
            Assert.That(fromCorners.Rect, Is.EqualTo(rect));
            Assert.That(fromSizeOnly.Rect, Is.EqualTo(new Rect(0, 0, 3, 4)));
        });
    }

    [Test]
    public void Equality_RespectsBothRectAndUnit()
    {
        var a = new RelativeRect(1, 2, 3, 4, RelativeUnit.Absolute);
        var b = new RelativeRect(1, 2, 3, 4, RelativeUnit.Absolute);
        var differentUnit = new RelativeRect(1, 2, 3, 4, RelativeUnit.Relative);
        var differentRect = new RelativeRect(0, 2, 3, 4, RelativeUnit.Absolute);

        Assert.Multiple(() =>
        {
            Assert.That(a == b, Is.True);
            Assert.That(a != differentUnit, Is.True);
            Assert.That(a != differentRect, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)"foo"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void ToPixels_Absolute_ReturnsRawRect()
    {
        var rr = new RelativeRect(10, 20, 30, 40, RelativeUnit.Absolute);
        Assert.That(rr.ToPixels(new Size(100, 200)), Is.EqualTo(new Rect(10, 20, 30, 40)));
    }

    [Test]
    public void ToPixels_Relative_ScalesAllComponents()
    {
        var rr = new RelativeRect(0.1f, 0.2f, 0.5f, 0.25f, RelativeUnit.Relative);
        Assert.That(rr.ToPixels(new Size(100, 400)), Is.EqualTo(new Rect(10, 80, 50, 100)));
    }

    [Test]
    public void Parse_AbsoluteRectangle()
    {
        var rr = RelativeRect.Parse("1,2,3,4");
        Assert.Multiple(() =>
        {
            Assert.That(rr.Unit, Is.EqualTo(RelativeUnit.Absolute));
            Assert.That(rr.Rect, Is.EqualTo(new Rect(1, 2, 3, 4)));
        });
    }

    [Test]
    public void Parse_RelativeRectangleAllPercent()
    {
        var rr = RelativeRect.Parse("10%, 20%, 50%, 25%");
        Assert.Multiple(() =>
        {
            Assert.That(rr.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(rr.Rect.X, Is.EqualTo(0.1f).Within(1e-6));
            Assert.That(rr.Rect.Y, Is.EqualTo(0.2f).Within(1e-6));
            Assert.That(rr.Rect.Width, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(rr.Rect.Height, Is.EqualTo(0.25f).Within(1e-6));
        });
    }

    [Test]
    public void Parse_MixedUnit_Throws()
    {
        Assert.Throws<FormatException>(() => RelativeRect.Parse("10%, 20, 50%, 25%"));
    }

    [Test]
    public void TryParse_ReturnsFalseOnGarbage()
    {
        Assert.That(RelativeRect.TryParse("nope", out RelativeRect result), Is.False);
        Assert.That(result, Is.EqualTo(default(RelativeRect)));
    }

    [Test]
    public void TryParse_AbsoluteString()
    {
        Assert.That(RelativeRect.TryParse("0,0,10,20", out RelativeRect result), Is.True);
        Assert.That(result, Is.EqualTo(new RelativeRect(0, 0, 10, 20, RelativeUnit.Absolute)));
    }

    [Test]
    public void Parse_StringWithProvider_AcceptsAlternateSeparator()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var parsed = RelativeRect.Parse("1;2;3;4", fr);
        Assert.That(parsed, Is.EqualTo(new RelativeRect(1, 2, 3, 4, RelativeUnit.Absolute)));

        Assert.That(RelativeRect.TryParse("1;2;3;4", fr, out RelativeRect result), Is.True);
        Assert.That(result, Is.EqualTo(parsed));
    }

    [Test]
    public void Parse_Utf8_AbsoluteAndRelative()
    {
        var absolute = RelativeRect.Parse("1,2,3,4"u8);
        var relative = RelativeRect.Parse("10%, 20%, 50%, 25%"u8);

        Assert.Multiple(() =>
        {
            Assert.That(absolute.Unit, Is.EqualTo(RelativeUnit.Absolute));
            Assert.That(absolute.Rect, Is.EqualTo(new Rect(1, 2, 3, 4)));
            Assert.That(relative.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(relative.Rect.Width, Is.EqualTo(0.5f).Within(1e-6));
        });
    }

    [Test]
    public void Parse_Utf8_MixedUnitThrows()
    {
        Assert.Throws<FormatException>(() => RelativeRect.Parse("10%, 20, 50%, 25%"u8));
    }

    [Test]
    public void TryParse_Utf8_HandlesGarbage()
    {
        Assert.That(RelativeRect.TryParse("oops"u8, out RelativeRect _), Is.False);
        Assert.That(RelativeRect.TryParse("1,2,3,4"u8, out RelativeRect ok), Is.True);
        Assert.That(ok, Is.EqualTo(new RelativeRect(1, 2, 3, 4, RelativeUnit.Absolute)));
    }

    [Test]
    public void ToString_AbsoluteAndRelative()
    {
        var absolute = new RelativeRect(1.5f, 2.5f, 3.5f, 4.5f, RelativeUnit.Absolute);
        var relative = new RelativeRect(0.25f, 0.5f, 0.5f, 0.5f, RelativeUnit.Relative);

        Assert.Multiple(() =>
        {
            Assert.That(absolute.ToString(), Is.EqualTo(absolute.Rect.ToString()));
            Assert.That(relative.ToString(), Is.EqualTo("25%, 50%, 50%, 50%"));
        });
    }

    [Test]
    public void ToString_WithProvider_UsesProviderSeparator()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var relative = new RelativeRect(0.25f, 0.5f, 0.5f, 0.5f, RelativeUnit.Relative);
        var absolute = new RelativeRect(1.5f, 2.5f, 3.5f, 4.5f, RelativeUnit.Absolute);

        Assert.Multiple(() =>
        {
            Assert.That(relative.ToString(fr), Does.Contain(";").And.Contain("%"));
            Assert.That(absolute.ToString(fr), Does.Contain(";"));
            Assert.That(relative.ToString("g", fr), Is.EqualTo(relative.ToString(fr)));
        });
    }

    [Test]
    public void TryFormat_Char_AndUtf8_ReturnSameContent()
    {
        var rects = new[]
        {
            new RelativeRect(1.5f, 2.5f, 3.5f, 4.5f, RelativeUnit.Absolute),
            new RelativeRect(0.25f, 0.5f, 0.5f, 0.5f, RelativeUnit.Relative),
        };

        Span<char> chars = stackalloc char[64];
        Span<byte> bytes = stackalloc byte[64];

        foreach (RelativeRect rr in rects)
        {
            Assert.That(rr.TryFormat(chars, out int cw, default, CultureInfo.InvariantCulture), Is.True);
            Assert.That(rr.TryFormat(bytes, out int bw, default, CultureInfo.InvariantCulture), Is.True);

            Assert.That(chars[..cw].ToString(), Is.EqualTo(Encoding.UTF8.GetString(bytes[..bw])));
        }
    }
}
