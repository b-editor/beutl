using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Media;

namespace Beutl.UnitTests.Editor;

// CPU tests for export supersampling pre-validation. Blocks Encode when the surface exceeds MaxBufferDimension.
[TestFixture]
public class ExportSupersamplingTests
{
    [TestCase(1920, 1080, 1, 1920L, 1080L)]
    [TestCase(1920, 1080, 4, 7680L, 4320L)]
    [TestCase(7680, 4320, 4, 30720L, 17280L)]
    public void GetRenderSize_MultipliesBothAxes(int w, int h, int factor, long expectedW, long expectedH)
    {
        (long width, long height) = ExportSupersampling.GetRenderSize(new PixelSize(w, h), factor);

        Assert.That(width, Is.EqualTo(expectedW));
        Assert.That(height, Is.EqualTo(expectedH));
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void GetRenderSize_FactorBelowOne_ClampsToOne(int factor)
    {
        // Mirrors `renderScale = Math.Max(1, SupersampleFactor)` in OutputViewModel.StartEncode.
        (long width, long height) = ExportSupersampling.GetRenderSize(new PixelSize(1920, 1080), factor);

        Assert.That((width, height), Is.EqualTo((1920L, 1080L)));
    }

    [Test]
    public void GetRenderSize_ExtremeFrameSize_DoesNotOverflow()
    {
        (long width, long _) = ExportSupersampling.GetRenderSize(new PixelSize(int.MaxValue, 1), 4);

        Assert.That(width, Is.EqualTo(int.MaxValue * 4L));
    }

    // Motivating case: 8K UHD at 4× needs 30720 px on the long axis, over the 16384 px per-axis GPU
    // limit, while 2× (15360 px) still fits.
    [TestCase(7680, 4320, 1, true)]
    [TestCase(7680, 4320, 2, true)]
    [TestCase(7680, 4320, 4, false)]
    [TestCase(1920, 1080, 4, true)]
    [TestCase(3840, 2160, 4, true)] // 4K × 4 = 15360 ≤ 16384
    [TestCase(4100, 2160, 4, false)] // width axis alone exceeds: 16400 > 16384
    [TestCase(2160, 4100, 4, false)] // ...and the height axis alone, too
    public void FitsBufferLimit_AgainstEngineLimit(int w, int h, int factor, bool expected)
    {
        Assert.That(ExportSupersampling.FitsBufferLimit(new PixelSize(w, h), factor), Is.EqualTo(expected));
    }

    [Test]
    public void FitsBufferLimit_DefaultLimit_IsTheEngineConstant()
    {
        var atLimit = new PixelSize(RenderScaleUtilities.MaxBufferDimension, 1080);
        var overLimit = new PixelSize(RenderScaleUtilities.MaxBufferDimension + 1, 1080);

        Assert.That(ExportSupersampling.FitsBufferLimit(atLimit, 1), Is.True);
        Assert.That(ExportSupersampling.FitsBufferLimit(overLimit, 1), Is.False);
    }

    [Test]
    public void FitsBufferLimit_CustomLimit_IsRespected()
    {
        Assert.That(ExportSupersampling.FitsBufferLimit(new PixelSize(50, 50), 2, maxDimension: 100), Is.True);
        Assert.That(ExportSupersampling.FitsBufferLimit(new PixelSize(51, 50), 2, maxDimension: 100), Is.False);
    }
}
