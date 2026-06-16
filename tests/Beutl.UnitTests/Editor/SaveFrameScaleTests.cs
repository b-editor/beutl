using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// CPU tests for save-frame scale-choice pre-validation. The dialog disables Save when
// the surface exceeds MaxBufferDimension.
[TestFixture]
public class SaveFrameScaleTests
{
    [TestCase(1920, 1080, 1f, 1920L, 1080L)]
    [TestCase(1920, 1080, 2f, 3840L, 2160L)]
    [TestCase(1920, 1080, 4f, 7680L, 4320L)]
    [TestCase(1920, 1080, 0.5f, 960L, 540L)]
    public void GetRenderSize_ScalesBothAxes(int w, int h, float scale, long expectedW, long expectedH)
    {
        (long width, long height) = SaveFrameScale.GetRenderSize(new PixelSize(w, h), scale);

        Assert.That((width, height), Is.EqualTo((expectedW, expectedH)));
    }

    [Test]
    public void GetRenderSize_FractionalScale_CeilsBothAxes()
    {
        // ceil(1921 * 0.5) = 961
        (long width, long height) = SaveFrameScale.GetRenderSize(new PixelSize(1921, 1081), 0.5f);

        Assert.That((width, height), Is.EqualTo((961L, 541L)));
    }

    [TestCase(0f)]
    [TestCase(-2f)]
    public void GetRenderSize_NonPositiveScale_ClampsToFloor(float scale)
    {
        // Degenerate multiplier clamps to the 1/64 floor.
        (long width, long height) = SaveFrameScale.GetRenderSize(new PixelSize(1920, 1080), scale);

        Assert.That((width, height), Is.EqualTo((30L, 17L))); // ceil(1920/64)=30, ceil(1080/64)=16.875→17
    }

    [Test]
    public void GetRenderSize_ExtremeFrameSize_DoesNotOverflow()
    {
        (long width, long _) = SaveFrameScale.GetRenderSize(new PixelSize(int.MaxValue, 1), 4f);

        Assert.That(width, Is.EqualTo(int.MaxValue * 4L));
    }

    // 8K at 4x = 30720 px > 16384 limit; 2x = 15360 fits.
    [TestCase(7680, 4320, 1f, true)]
    [TestCase(7680, 4320, 2f, true)]
    [TestCase(7680, 4320, 4f, false)]
    [TestCase(1920, 1080, 4f, true)]
    [TestCase(3840, 2160, 4f, true)] // 4K × 4 = 15360 ≤ 16384
    [TestCase(4100, 2160, 4f, false)] // width axis alone exceeds: 16400 > 16384
    public void FitsBufferLimit_AgainstEngineLimit(int w, int h, float scale, bool expected)
    {
        Assert.That(SaveFrameScale.FitsBufferLimit(new PixelSize(w, h), scale), Is.EqualTo(expected));
    }

    [Test]
    public void FitsBufferLimit_DefaultLimit_IsTheEngineConstant()
    {
        var atLimit = new PixelSize(RenderNodeContext.MaxBufferDimension, 1080);
        var overLimit = new PixelSize(RenderNodeContext.MaxBufferDimension + 1, 1080);

        Assert.That(SaveFrameScale.FitsBufferLimit(atLimit, 1f), Is.True);
        Assert.That(SaveFrameScale.FitsBufferLimit(overLimit, 1f), Is.False);
    }

    [Test]
    public void Factors_AreTheExpectedMultipliers()
    {
        Assert.That(SaveFrameScale.Factors, Is.EqualTo(new[] { 0.5f, 1f, 2f, 4f }));
    }

    // A non-empty source always yields >= 1 px/axis; only a 0-area source fails.
    [TestCase(1920, 1080, 1f, true)]
    [TestCase(1920, 1080, 0.5f, true)]
    [TestCase(1, 1, 0.5f, true)] // ceil(1 × 0.5) = 1 on each axis
    [TestCase(0, 0, 1f, false)]
    [TestCase(0, 1080, 1f, false)]
    [TestCase(1920, 0, 4f, false)]
    public void ProducesRenderableSurface_RejectsZeroAreaSource(int w, int h, float scale, bool expected)
    {
        Assert.That(SaveFrameScale.ProducesRenderableSurface(new PixelSize(w, h), scale), Is.EqualTo(expected));
    }
}
