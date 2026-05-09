using System.Globalization;
using System.Text;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class ThicknessTests
{
    [Test]
    public void Constructor_Uniform_AllSidesEqual()
    {
        var t = new Thickness(5);
        Assert.That(t.Left, Is.EqualTo(5));
        Assert.That(t.Top, Is.EqualTo(5));
        Assert.That(t.Right, Is.EqualTo(5));
        Assert.That(t.Bottom, Is.EqualTo(5));
        Assert.That(t.IsUniform, Is.True);
    }

    [Test]
    public void Constructor_HorizontalVertical_PairsSides()
    {
        var t = new Thickness(2, 3);
        Assert.That(t.Left, Is.EqualTo(2));
        Assert.That(t.Right, Is.EqualTo(2));
        Assert.That(t.Top, Is.EqualTo(3));
        Assert.That(t.Bottom, Is.EqualTo(3));
    }

    [Test]
    public void Constructor_FourValues_AssignsEachSide()
    {
        var t = new Thickness(1, 2, 3, 4);
        Assert.That(t.Left, Is.EqualTo(1));
        Assert.That(t.Top, Is.EqualTo(2));
        Assert.That(t.Right, Is.EqualTo(3));
        Assert.That(t.Bottom, Is.EqualTo(4));
        Assert.That(t.IsUniform, Is.False);
        Assert.That(t.IsDefault, Is.False);
    }

    [Test]
    public void IsEmpty_AndIsDefault_TrueOnlyForZero()
    {
        Assert.That(new Thickness(0).IsEmpty, Is.True);
        Assert.That(new Thickness(0).IsDefault, Is.True);
        Assert.That(new Thickness(1).IsEmpty, Is.False);
        Assert.That(new Thickness(1, 2, 3, 4).IsDefault, Is.False);
    }

    [Test]
    public void Equality_ChecksAllSides()
    {
        var a = new Thickness(1, 2, 3, 4);
        var b = new Thickness(1, 2, 3, 4);
        var c = new Thickness(0, 2, 3, 4);
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"x"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void AddSubtractMultiply_ProducePerSideResults()
    {
        var a = new Thickness(1, 2, 3, 4);
        var b = new Thickness(10, 20, 30, 40);

        Assert.That(a + b, Is.EqualTo(new Thickness(11, 22, 33, 44)));
        Assert.That(b - a, Is.EqualTo(new Thickness(9, 18, 27, 36)));
        Assert.That(a * 2f, Is.EqualTo(new Thickness(2, 4, 6, 8)));
    }

    [Test]
    public void Deconstruct_ExposesEachSide()
    {
        var (l, t, r, b) = new Thickness(1, 2, 3, 4);
        Assert.That(l, Is.EqualTo(1));
        Assert.That(t, Is.EqualTo(2));
        Assert.That(r, Is.EqualTo(3));
        Assert.That(b, Is.EqualTo(4));
    }

    [Test]
    public void WithMethods_OnlyChangeOneSide()
    {
        var t = new Thickness(1, 2, 3, 4);
        Assert.That(t.WithLeft(9), Is.EqualTo(new Thickness(9, 2, 3, 4)));
        Assert.That(t.WithTop(9), Is.EqualTo(new Thickness(1, 9, 3, 4)));
        Assert.That(t.WithRight(9), Is.EqualTo(new Thickness(1, 2, 9, 4)));
        Assert.That(t.WithBottom(9), Is.EqualTo(new Thickness(1, 2, 3, 9)));
    }

    [Test]
    public void ToString_AdaptsToUniformity()
    {
        Assert.That(new Thickness(5).ToString(), Is.EqualTo("5"));
        Assert.That(new Thickness(2, 3).ToString(), Is.EqualTo("2, 3"));
        Assert.That(new Thickness(1, 2, 3, 4).ToString(), Is.EqualTo("1, 2, 3, 4"));
    }

    [Test]
    public void Parse_ConvertsOneTwoOrFourValues()
    {
        Assert.That(Thickness.Parse("5"), Is.EqualTo(new Thickness(5)));
        Assert.That(Thickness.Parse("2, 3"), Is.EqualTo(new Thickness(2, 3)));
        Assert.That(Thickness.Parse("1, 2, 3, 4"), Is.EqualTo(new Thickness(1, 2, 3, 4)));
    }

    [Test]
    public void Parse_WithProvider_AcceptsAlternateSeparator()
    {
        var fr = CultureInfo.GetCultureInfo("fr");
        Assert.That(Thickness.Parse("1; 2; 3; 4", fr), Is.EqualTo(new Thickness(1, 2, 3, 4)));
    }

    [Test]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.That(Thickness.TryParse("garbage", out Thickness t), Is.False);
        Assert.That(t, Is.EqualTo(default(Thickness)));
        Assert.That(Thickness.TryParse(""u8, out _), Is.False);
    }

    [Test]
    public void Parse_Utf8_RoundTrip()
    {
        Assert.That(Thickness.Parse("1, 2, 3, 4"u8), Is.EqualTo(new Thickness(1, 2, 3, 4)));
    }

    [Test]
    public void TryFormat_Char_AndUtf8_ReturnSameContent()
    {
        var values = new[]
        {
            new Thickness(5),
            new Thickness(2, 3),
            new Thickness(1, 2, 3, 4),
        };

        foreach (Thickness t in values)
        {
            Span<char> chars = stackalloc char[64];
            Span<byte> bytes = stackalloc byte[64];

            Assert.That(t.TryFormat(chars, out int cw), Is.True);
            Assert.That(t.TryFormat(bytes, out int bw), Is.True);

            Assert.That(chars[..cw].ToString(),
                Is.EqualTo(Encoding.UTF8.GetString(bytes[..bw])));
        }
    }
}
