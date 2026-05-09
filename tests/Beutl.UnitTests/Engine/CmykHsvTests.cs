using Beutl.Media;

namespace Beutl.UnitTests.Engine;

public class CmykHsvTests
{
    [Test]
    public void Cmyk_Constructor_StoresComponents()
    {
        var c = new Cmyk(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
        Assert.Multiple(() =>
        {
            Assert.That(c.C, Is.EqualTo(0.1f));
            Assert.That(c.M, Is.EqualTo(0.2f));
            Assert.That(c.Y, Is.EqualTo(0.3f));
            Assert.That(c.K, Is.EqualTo(0.4f));
            Assert.That(c.A, Is.EqualTo(0.5f));
        });
    }

    [Test]
    public void Cmyk_Equality_AndHashCode()
    {
        var a = new Cmyk(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
        var b = new Cmyk(0.1f, 0.2f, 0.3f, 0.4f, 0.5f);
        var different = new Cmyk(0.1f, 0.2f, 0.3f, 0.4f, 1f);

        Assert.Multiple(() =>
        {
            Assert.That(a == b, Is.True);
            Assert.That(a != different, Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.Equals((object)42), Is.False);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void Cmyk_FromColor_RoundTripsThroughColor()
    {
        var color = Color.FromArgb(0x40, 0x20, 0x80, 0xC0);
        var cmyk = new Cmyk(color);
        Assert.That(cmyk.ToColor(), Is.EqualTo(color));
    }

    [Test]
    public void Cmyk_FromHsv_MatchesColorRouting()
    {
        var color = Color.FromArgb(0xff, 0x12, 0x34, 0x56);
        Hsv hsv = color.ToHsv();
        Cmyk fromHsvCtor = new Cmyk(hsv);
        Cmyk viaToCmyk = hsv.ToCmyk();
        Assert.That(fromHsvCtor, Is.EqualTo(viaToCmyk));
    }

    [Test]
    public void Cmyk_ToColor_PureBlackAndWhite()
    {
        var black = new Cmyk(0, 0, 0, 1, 1).ToColor();
        var white = new Cmyk(0, 0, 0, 0, 1).ToColor();
        Assert.Multiple(() =>
        {
            Assert.That(black, Is.EqualTo(Color.FromArgb(255, 0, 0, 0)));
            Assert.That(white, Is.EqualTo(Color.FromArgb(255, 255, 255, 255)));
        });
    }

    [Test]
    public void Cmyk_ToHsv_RoundTripsBackToColor()
    {
        var color = Color.FromArgb(0xff, 0x80, 0x40, 0xC0);
        Cmyk cmyk = color.ToCmyk();
        Hsv hsv = cmyk.ToHsv();
        Assert.That(hsv.ToColor(), Is.EqualTo(color));
    }

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

    [Test]
    public void Hsv_FromCmyk_MatchesColorRouting()
    {
        var color = Color.FromArgb(0xff, 0x40, 0xA0, 0x60);
        Cmyk cmyk = color.ToCmyk();
        Hsv direct = cmyk.ToHsv();
        Hsv viaCtor = new Hsv(cmyk);
        Assert.That(direct, Is.EqualTo(viaCtor));
    }

    [Test]
    public void Hsv_ToCmyk_RoundTripsBackToColor()
    {
        var color = Color.FromArgb(0xff, 0x60, 0x90, 0x10);
        Hsv hsv = color.ToHsv();
        Cmyk cmyk = hsv.ToCmyk();
        Assert.That(cmyk.ToColor(), Is.EqualTo(color));
    }
}
