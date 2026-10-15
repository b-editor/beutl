using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class CmykHsvTests
{
    [Test]
    public void Hsv_Constructor_StoresComponents()
    {
        var h = new Hsv(120f, 50f, 75f, 1f);
        Assert.Multiple(() =>
        {
            Assert.That(h.H, Is.EqualTo(120f));
            Assert.That(h.S, Is.EqualTo(50f));
            Assert.That(h.V, Is.EqualTo(75f));
            Assert.That(h.A, Is.EqualTo(1f));
        });
    }

    [Test]
    public void Hsv_Equality_AndHashCode()
    {
        var a = new Hsv(120f, 50f, 75f, 1f);
        var b = new Hsv(120f, 50f, 75f, 1f);
        var differentHue = new Hsv(180f, 50f, 75f, 1f);

        Assert.Multiple(() =>
        {
            Assert.That(a == b, Is.True);
            Assert.That(a != differentHue, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)"foo"), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void Hsv_ToColor_ZeroSaturationProducesGray()
    {
        var hsv = new Hsv(180f, 0f, 50f, 1f);
        Color color = hsv.ToColor();
        Assert.Multiple(() =>
        {
            Assert.That(color.R, Is.EqualTo(color.G));
            Assert.That(color.G, Is.EqualTo(color.B));
            Assert.That(color.A, Is.EqualTo(0xff));
            Assert.That(color.R, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Hsv_ToColor_HueWrapsAt360()
    {
        var atZero = new Hsv(0f, 100f, 100f, 1f).ToColor();
        var atThreeSixty = new Hsv(360f, 100f, 100f, 1f).ToColor();
        Assert.That(atZero, Is.EqualTo(atThreeSixty));
    }

    [Test]
    [TestCase(0f, 255, 0, 0)]
    [TestCase(60f, 255, 255, 0)]
    [TestCase(120f, 0, 255, 0)]
    [TestCase(180f, 0, 255, 255)]
    [TestCase(240f, 0, 0, 255)]
    [TestCase(300f, 255, 0, 255)]
    public void Hsv_ToColor_PrimaryHues(float hue, int r, int g, int b)
    {
        var hsv = new Hsv(hue, 100f, 100f, 1f);
        var color = hsv.ToColor();
        Assert.Multiple(() =>
        {
            Assert.That(color.R, Is.EqualTo(r));
            Assert.That(color.G, Is.EqualTo(g));
            Assert.That(color.B, Is.EqualTo(b));
            Assert.That(color.A, Is.EqualTo(0xff));
        });
    }
}
