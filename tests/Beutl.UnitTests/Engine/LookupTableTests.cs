#pragma warning disable CS0618 // Type or member is obsolete

using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class LookupTableTests
{
    [Test]
    public void Linear_FillsIdentityRamp()
    {
        var data = new byte[256];
        LookupTable.Linear(data);

        for (int i = 0; i < 256; i++)
        {
            Assert.That(data[i], Is.EqualTo((byte)i));
        }
    }

    [Test]
    public void Linear_ThrowsOnWrongSize()
    {
        Assert.Throws<ArgumentException>(() => LookupTable.Linear(new byte[10]));
    }

    [Test]
    public void Invert_ReversesRamp()
    {
        var data = new byte[256];
        LookupTable.Invert(data);

        Assert.That(data[0], Is.EqualTo((byte)255));
        Assert.That(data[255], Is.EqualTo((byte)0));
        Assert.That(data[128], Is.EqualTo((byte)127));
    }

    [Test]
    public void Invert_ThrowsOnWrongSize()
    {
        Assert.Throws<ArgumentException>(() => LookupTable.Invert(new byte[10]));
    }

    [Test]
    public void SetStrength_OneIsNoOp()
    {
        var data = new byte[256];
        LookupTable.Invert(data);
        var copy = (byte[])data.Clone();

        LookupTable.SetStrength(1f, data);

        Assert.That(data, Is.EqualTo(copy));
    }

    [Test]
    public void SetStrength_ZeroBlendsToIdentity()
    {
        var data = new byte[256];
        LookupTable.Invert(data);

        LookupTable.SetStrength(0f, data);

        for (int i = 0; i < 256; i++)
        {
            Assert.That(data[i], Is.EqualTo((byte)i));
        }
    }

    [Test]
    public void SetStrength_ThrowsOnWrongSize()
    {
        Assert.Throws<ArgumentException>(() => LookupTable.SetStrength(0.5f, new byte[10]));
    }

    [Test]
    public void SetStrengthArgb_ZeroBlendsAllToIdentity()
    {
        var a = new byte[256];
        var r = new byte[256];
        var g = new byte[256];
        var b = new byte[256];
        LookupTable.Invert(a);
        LookupTable.Invert(r);
        LookupTable.Invert(g);
        LookupTable.Invert(b);

        LookupTable.SetStrength(0f, (a, r, g, b));

        for (int i = 0; i < 256; i++)
        {
            Assert.That(a[i], Is.EqualTo((byte)i));
            Assert.That(b[i], Is.EqualTo((byte)i));
        }
    }

    [Test]
    public void Solarisation_ProducesValidByteRange()
    {
        var data = new byte[256];
        LookupTable.Solarisation(data);

        for (int i = 0; i < 256; i++)
        {
            Assert.That(data[i], Is.InRange((byte)0, (byte)255));
        }
    }

    [Test]
    public void Negaposi_DefaultIs255MinusIndex()
    {
        var data = new byte[256];
        LookupTable.Negaposi(data);

        Assert.That(data[0], Is.EqualTo((byte)255));
        Assert.That(data[255], Is.EqualTo((byte)0));
    }

    [Test]
    public void Negaposi_WithCustomValue()
    {
        var data = new byte[256];
        LookupTable.Negaposi(data, 200);

        Assert.That(data[0], Is.EqualTo((byte)200));
    }

    [Test]
    public void Negaposi_RGB_AppliesPerChannel()
    {
        var r = new byte[256];
        var g = new byte[256];
        var b = new byte[256];

        LookupTable.Negaposi((r, g, b), 200, 150, 100);

        Assert.That(r[0], Is.EqualTo((byte)200));
        Assert.That(g[0], Is.EqualTo((byte)150));
        Assert.That(b[0], Is.EqualTo((byte)100));
    }

    [Test]
    public void Negaposi_RGB_SameValueOptimizationProducesIdenticalChannels()
    {
        var r = new byte[256];
        var g = new byte[256];
        var b = new byte[256];

        LookupTable.Negaposi((r, g, b), 255, 255, 255);

        Assert.That(g, Is.EqualTo(r));
        Assert.That(b, Is.EqualTo(r));
    }

    [Test]
    public void Contrast_ZeroProducesIdentity()
    {
        var data = new byte[256];
        LookupTable.Contrast(data, 0);

        for (int i = 0; i < 256; i++)
        {
            Assert.That(data[i], Is.EqualTo((byte)i));
        }
    }

    [Test]
    public void Contrast_ClampsRange()
    {
        var data = new byte[256];
        LookupTable.Contrast(data, 1000);

        Assert.That(data[0], Is.EqualTo((byte)0));
        Assert.That(data[255], Is.EqualTo((byte)255));
    }

    [Test]
    public void Gamma_OneIsIdentity()
    {
        var data = new byte[256];
        LookupTable.Gamma(data, 1f);

        for (int i = 0; i < 256; i++)
        {
            Assert.That(data[i], Is.EqualTo((byte)i).Within((byte)1));
        }
    }

    [Test]
    public void Gamma_ClampsBelowMin()
    {
        var data = new byte[256];
        Assert.DoesNotThrow(() => LookupTable.Gamma(data, 0f));
    }

    [Test]
    public void Solarisation_ThrowsOnWrongSize()
    {
        Assert.Throws<ArgumentException>(() => LookupTable.Solarisation(new byte[10]));
    }

    [Test]
    public void Negaposi_ThrowsOnWrongSize()
    {
        Assert.Throws<ArgumentException>(() => LookupTable.Negaposi(new byte[10]));
    }

    [Test]
    public void Contrast_ThrowsOnWrongSize()
    {
        Assert.Throws<ArgumentException>(() => LookupTable.Contrast(new byte[10], 0));
    }

    [Test]
    public void Gamma_ThrowsOnWrongSize()
    {
        Assert.Throws<ArgumentException>(() => LookupTable.Gamma(new byte[10], 1f));
    }
}
