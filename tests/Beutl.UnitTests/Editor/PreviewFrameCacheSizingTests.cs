using Beutl.Graphics;
using Beutl.Media;
using Beutl.Models;

namespace Beutl.UnitTests.Editor;

// Tests for PreviewFrameCacheSizing.DeriveCacheSize. The player view model reapplies this mapping to every
// rebuilt FrameCacheManager, so it decides whether reduced caches survive a quality-switch / Fit-resize rebuild.
[TestFixture]
public class PreviewFrameCacheSizingTests
{
    private static readonly PixelSize Frame = new(1920, 1080);

    [Test]
    public void DeriveCacheSize_PanelHalfTheFrame_HalvesTheCacheSize()
    {
        // scale 0.5 -> den = 2 (already even) -> 1920x1080 / 2.
        PixelSize? size = PreviewFrameCacheSizing.DeriveCacheSize(new Size(960, 540), Frame);
        Assert.That(size, Is.EqualTo(new PixelSize(960, 540)));
    }

    [Test]
    public void DeriveCacheSize_OddDenominator_IsBumpedToEven()
    {
        // scale = 1/3 -> den = 3 -> bumped to 4 -> 480x270.
        PixelSize? size = PreviewFrameCacheSizing.DeriveCacheSize(new Size(640, 360), Frame);
        Assert.That(size, Is.EqualTo(new PixelSize(480, 270)));
    }

    [Test]
    public void DeriveCacheSize_PanelJustBelowFrameSize_StillCachesAtHalf()
    {
        // scale 0.9 -> den = (int)(1 / 0.9) = 1 -> bumped to 2: any panel smaller than the frame
        // caches at most at half size (matches the historical MaxFrameSize setter).
        PixelSize? size = PreviewFrameCacheSizing.DeriveCacheSize(new Size(1728, 972), Frame);
        Assert.That(size, Is.EqualTo(new PixelSize(960, 540)));
    }

    [Test]
    public void DeriveCacheSize_NonUniformPanel_UsesTheSmallerFitAxis()
    {
        // Stretch.Uniform: scaleX = 0.5, scaleY = 0.25 -> min = 0.25 -> den = 4 -> 480x270.
        PixelSize? size = PreviewFrameCacheSizing.DeriveCacheSize(new Size(960, 270), Frame);
        Assert.That(size, Is.EqualTo(new PixelSize(480, 270)));
    }

    [TestCase(1920, 1080, Description = "exactly frame-sized")]
    [TestCase(3840, 2160, Description = "larger than the frame")]
    public void DeriveCacheSize_PanelAtLeastFrameSized_ReturnsNull(int width, int height)
    {
        // scale >= 1 must use the original size; it must never reach (int)(1 / scale) == 0,
        // which would make 1 / den +Infinity and produce an int.MaxValue-sized entry.
        Assert.That(PreviewFrameCacheSizing.DeriveCacheSize(new Size(width, height), Frame), Is.Null);
    }

    [TestCase(0, 0, Description = "panel not laid out yet (default Size)")]
    [TestCase(0, 540, Description = "degenerate width")]
    [TestCase(960, 0, Description = "degenerate height")]
    public void DeriveCacheSize_DegeneratePanel_ReturnsNull(int width, int height)
    {
        Assert.That(PreviewFrameCacheSizing.DeriveCacheSize(new Size(width, height), Frame), Is.Null);
    }

    [Test]
    public void DeriveCacheSize_DegenerateFrame_ReturnsNull()
    {
        // A zero-sized frame makes CalculateScaling return 0; deriving must not divide by it.
        Assert.That(PreviewFrameCacheSizing.DeriveCacheSize(new Size(960, 540), new PixelSize(0, 0)), Is.Null);
    }
}
