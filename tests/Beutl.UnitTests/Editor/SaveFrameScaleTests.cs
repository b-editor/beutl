using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Media;

namespace Beutl.UnitTests.Editor;

// CPU unit tests for the save-frame scale-choice pre-validation (feature 003, US4 follow-up). The save
// path renders the current frame / element onto a ceil(FrameSize × scale) surface; a surface larger than
// RenderNodeContext.MaxBufferDimension on either axis cannot be allocated, so the dialog disables Save
// up-front via this pure helper instead of failing mid-render with a generic error.
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
        // Mirrors Renderer.DeviceSize = ceil(FrameSize × scale): 1921 × 0.5 = 960.5 → 961.
        (long width, long height) = SaveFrameScale.GetRenderSize(new PixelSize(1921, 1081), 0.5f);

        Assert.That((width, height), Is.EqualTo((961L, 541L)));
    }

    [TestCase(0f)]
    [TestCase(-2f)]
    public void GetRenderSize_NonPositiveScale_ClampsToFloor(float scale)
    {
        // A degenerate multiplier must never size the surface 0×0; it clamps to the 1/64 floor.
        (long width, long height) = SaveFrameScale.GetRenderSize(new PixelSize(1920, 1080), scale);

        Assert.That((width, height), Is.EqualTo((30L, 17L))); // ceil(1920/64)=30, ceil(1080/64)=16.875→17
    }

    [Test]
    public void GetRenderSize_ExtremeFrameSize_DoesNotOverflow()
    {
        (long width, long _) = SaveFrameScale.GetRenderSize(new PixelSize(int.MaxValue, 1), 4f);

        Assert.That(width, Is.EqualTo(int.MaxValue * 4L));
    }

    // The motivating case: an 8K UHD frame at 4× needs 30720 px on the long axis — over the 16384 px
    // per-axis GPU limit — while 2× (15360 px) still fits.
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

    // A non-empty source always produces a renderable surface (>= 1 px/axis), even at the 0.5 floor; only a
    // degenerate 0-area source (an element that renders nothing) does not, so the save path must not offer it.
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
