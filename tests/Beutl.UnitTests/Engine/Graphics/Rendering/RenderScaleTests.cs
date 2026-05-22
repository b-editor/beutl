using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RenderScaleTests
{
    [Test]
    public void Identity_IsOneOnBothAxes()
    {
        Assert.That(RenderScale.Identity.ScaleX, Is.EqualTo(1f));
        Assert.That(RenderScale.Identity.ScaleY, Is.EqualTo(1f));
        Assert.That(RenderScale.Identity.IsIdentity, Is.True);
    }

    [Test]
    public void FromRatio_ProducesUniformScale()
    {
        var s = RenderScale.FromRatio(4f);
        Assert.That(s.ScaleX, Is.EqualTo(4f));
        Assert.That(s.ScaleY, Is.EqualTo(4f));
        Assert.That(s.IsIdentity, Is.False);
    }

    [Test]
    public void FromFrames_ComputesBoundsOverRasterPerAxis()
    {
        var s = RenderScale.FromFrames(raster: new PixelSize(480, 270), bounds: new PixelSize(1920, 1080));
        Assert.That(s.ScaleX, Is.EqualTo(4f));
        Assert.That(s.ScaleY, Is.EqualTo(4f));
    }

    [Test]
    public void FromFrames_AllowsRasterEqualToBounds()
    {
        var s = RenderScale.FromFrames(raster: new PixelSize(1920, 1080), bounds: new PixelSize(1920, 1080));
        Assert.That(s, Is.EqualTo(RenderScale.Identity));
    }

    [TestCase(0f, 1f)]
    [TestCase(1f, 0f)]
    [TestCase(0.5f, 1f)]
    [TestCase(1f, 0.5f)]
    [TestCase(-1f, 1f)]
    [TestCase(float.NaN, 1f)]
    [TestCase(float.PositiveInfinity, 1f)]
    [TestCase(1f, float.NegativeInfinity)]
    public void Constructor_RejectsInvalidValues(float scaleX, float scaleY)
    {
        Assert.That(() => new RenderScale(scaleX, scaleY), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void FromFrames_RejectsRasterLargerThanBounds()
    {
        Assert.That(
            () => RenderScale.FromFrames(raster: new PixelSize(1920, 1080), bounds: new PixelSize(480, 270)),
            Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 1)]
    public void FromFrames_RejectsNonPositiveDimensions(int rasterW, int rasterH)
    {
        Assert.That(
            () => RenderScale.FromFrames(raster: new PixelSize(rasterW, rasterH), bounds: new PixelSize(1920, 1080)),
            Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ToRasterX_DividesByScaleX()
    {
        var s = new RenderScale(4f, 2f);
        Assert.That(s.ToRasterX(20f), Is.EqualTo(5f));
    }

    [Test]
    public void ToRasterY_DividesByScaleY()
    {
        var s = new RenderScale(4f, 2f);
        Assert.That(s.ToRasterY(20f), Is.EqualTo(10f));
    }

    [Test]
    public void ToAuthoringX_MultipliesByScaleX()
    {
        var s = new RenderScale(4f, 2f);
        Assert.That(s.ToAuthoringX(5f), Is.EqualTo(20f));
    }

    [Test]
    public void ToAuthoringY_MultipliesByScaleY()
    {
        var s = new RenderScale(4f, 2f);
        Assert.That(s.ToAuthoringY(10f), Is.EqualTo(20f));
    }

    [Test]
    public void ToRaster_Size_DividesPerAxis()
    {
        var s = new RenderScale(4f, 2f);
        var r = s.ToRaster(new Size(20f, 10f));
        Assert.That(r.Width, Is.EqualTo(5f));
        Assert.That(r.Height, Is.EqualTo(5f));
    }

    [Test]
    public void ToRaster_Point_DividesPerAxis()
    {
        var s = new RenderScale(4f, 2f);
        var p = s.ToRaster(new Point(20f, 10f));
        Assert.That(p.X, Is.EqualTo(5f));
        Assert.That(p.Y, Is.EqualTo(5f));
    }

    [Test]
    public void ToRasterUniform_UsesGeometricMean()
    {
        var s = new RenderScale(4f, 1f);
        // geometric mean = sqrt(4) = 2; ToRasterUniform(10) = 10 / 2 = 5
        Assert.That(s.ToRasterUniform(10f), Is.EqualTo(5f).Within(1e-5f));
    }

    [Test]
    public void Equality_ByValue()
    {
        var a = new RenderScale(4f, 2f);
        var b = new RenderScale(4f, 2f);
        var c = new RenderScale(2f, 4f);

        Assert.That(a, Is.EqualTo(b));
        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void IsIdentity_TrueOnlyForExactOne()
    {
        Assert.That(new RenderScale(1f, 1f).IsIdentity, Is.True);
        Assert.That(new RenderScale(1.0001f, 1f).IsIdentity, Is.False);
    }
}
