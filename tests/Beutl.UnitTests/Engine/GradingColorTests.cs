using System.Numerics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class GradingColorTests
{
    [Test]
    public void StaticMembers_AreExpected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GradingColor.Zero, Is.EqualTo(new GradingColor(0, 0, 0)));
            Assert.That(GradingColor.One, Is.EqualTo(new GradingColor(1, 1, 1)));
        });
    }

    [Test]
    public void FromRgb_AndFromVector3_AreEquivalent()
    {
        Assert.That(GradingColor.FromRgb(0.1f, 0.2f, 0.3f),
            Is.EqualTo(GradingColor.FromVector3(new Vector3(0.1f, 0.2f, 0.3f))));
    }

    [Test]
    public void FromColor_DivBy255_ProducesNormalizedComponents()
    {
        var color = Color.FromArgb(255, 51, 102, 204);
        var grading = GradingColor.FromColor(color);

        Assert.Multiple(() =>
        {
            Assert.That(grading.R, Is.EqualTo(51f / 255f).Within(1e-6));
            Assert.That(grading.G, Is.EqualTo(102f / 255f).Within(1e-6));
            Assert.That(grading.B, Is.EqualTo(204f / 255f).Within(1e-6));
        });
    }

    [Test]
    public void ToVector3_AndToColor_ConvertCorrectly()
    {
        var grading = new GradingColor(0.2f, 0.4f, 1f);

        Assert.Multiple(() =>
        {
            Assert.That(grading.ToVector3(), Is.EqualTo(new Vector3(0.2f, 0.4f, 1f)));
            var converted = grading.ToColor();
            Assert.That(converted.A, Is.EqualTo(255));
            Assert.That(converted.R, Is.EqualTo((byte)Math.Clamp(0.2f * 255f, 0f, 255f)));
            Assert.That(converted.G, Is.EqualTo((byte)Math.Clamp(0.4f * 255f, 0f, 255f)));
            Assert.That(converted.B, Is.EqualTo((byte)Math.Clamp(1f * 255f, 0f, 255f)));
        });
    }

    [Test]
    public void ToColor_ClampsOutOfRangeComponents()
    {
        var negative = new GradingColor(-0.5f, 0.5f, 1.5f).ToColor();
        Assert.Multiple(() =>
        {
            Assert.That(negative.R, Is.EqualTo(0));
            Assert.That(negative.B, Is.EqualTo(255));
        });
    }

    [Test]
    public void Parse_ValidString_ReturnsExpected()
    {
        var color = GradingColor.Parse("0.25, 0.5, 0.75");
        Assert.That(color, Is.EqualTo(new GradingColor(0.25f, 0.5f, 0.75f)));
    }

    [Test]
    public void Parse_NullString_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GradingColor.Parse((string)null!));
    }

    [Test]
    public void Parse_InvalidString_Throws()
    {
        Assert.Throws<FormatException>(() => GradingColor.Parse("not-a-color"));
        Assert.Throws<FormatException>(() => GradingColor.Parse("1,2"));
    }

    [Test]
    public void TryParse_HandlesEmptyAndWhitespace()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GradingColor.TryParse((string?)null, out _), Is.False);
            Assert.That(GradingColor.TryParse("", out _), Is.False);
            Assert.That(GradingColor.TryParse("   ", out _), Is.False);
        });
    }

    [Test]
    public void TryParse_ValidString_Succeeds()
    {
        Assert.That(GradingColor.TryParse(" 1, 2, 3 ", out GradingColor color), Is.True);
        Assert.That(color, Is.EqualTo(new GradingColor(1, 2, 3)));
    }

    [Test]
    public void Equality_AndOperators()
    {
        var a = new GradingColor(1, 2, 3);
        var b = new GradingColor(1, 2, 3);
        var c = new GradingColor(4, 5, 6);

        Assert.Multiple(() =>
        {
            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)"foo"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void Operators_AddSubtractScale()
    {
        var a = new GradingColor(0.5f, 1.5f, 2.5f);
        var b = new GradingColor(1f, 2f, 3f);

        GradingColor sum = a + b;
        GradingColor diff = b - a;
        GradingColor scaledA = a * 2f;
        GradingColor scaledB = 2f * a;

        Assert.Multiple(() =>
        {
            Assert.That(sum.R, Is.EqualTo(1.5f).Within(1e-6));
            Assert.That(sum.G, Is.EqualTo(3.5f).Within(1e-6));
            Assert.That(sum.B, Is.EqualTo(5.5f).Within(1e-6));
            Assert.That(diff.R, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(diff.G, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(diff.B, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(scaledA.R, Is.EqualTo(1f).Within(1e-6));
            Assert.That(scaledA.B, Is.EqualTo(5f).Within(1e-6));
            Assert.That(scaledB, Is.EqualTo(scaledA));
        });
    }

    [Test]
    public void ToString_UsesInvariantCulture()
    {
        Assert.That(new GradingColor(1.5f, 2.5f, 3.5f).ToString(), Is.EqualTo("1.5, 2.5, 3.5"));
    }
}
