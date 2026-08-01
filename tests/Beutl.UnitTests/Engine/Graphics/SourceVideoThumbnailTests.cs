using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class SourceVideoThumbnailTests
{
    // Regression for Project #9 item #240 (Grafana Loki 2026-06-14): a degenerate frame size
    // (Height == 0, e.g. corrupt media metadata) made GetThumbnailStripAsync compute count = 0,
    // interval = +Infinity, and TimeSpan.FromSeconds(interval * 0.5) threw OverflowException
    // ("TimeSpan overflowed because the duration is too long"), crashing thumbnail generation.
    // GetVideoThumbnailCount must return 0 so the caller bails out before that math runs.
    [TestCase(1920, 0, TestName = "ZeroHeight")]
    [TestCase(1920, -1, TestName = "NegativeHeight")]
    [TestCase(0, 1080, TestName = "ZeroWidth")]
    [TestCase(-1, 1080, TestName = "NegativeWidth")]
    public void GetVideoThumbnailCount_ReturnsZero_ForDegenerateFrameSize(int width, int height)
    {
        int count = SourceVideo.GetVideoThumbnailCount(new PixelSize(width, height), maxWidth: 1920, maxHeight: 25);

        Assert.That(count, Is.Zero);
    }

    [Test]
    public void GetVideoThumbnailCount_ReturnsZero_ForNonPositiveMaxDimensions()
    {
        var frame = new PixelSize(1920, 1080);
        Assert.That(SourceVideo.GetVideoThumbnailCount(frame, maxWidth: 0, maxHeight: 25), Is.Zero);
        Assert.That(SourceVideo.GetVideoThumbnailCount(frame, maxWidth: 1920, maxHeight: 0), Is.Zero);
    }

    [Test]
    public void GetVideoThumbnailCount_ComputesExpectedCount_ForValidFrameSize()
    {
        // 16:9 frame, maxWidth 1920, thumbnail height 25:
        //   thumbWidth = 25 * (1920 / 1080) ≈ 44.44, count = ceil(1920 / 44.44) = 44.
        int count = SourceVideo.GetVideoThumbnailCount(new PixelSize(1920, 1080), maxWidth: 1920, maxHeight: 25);

        Assert.That(count, Is.EqualTo(44));
    }

    // Pins the failure mode the guard prevents: a zero count makes the interval +Infinity and
    // TimeSpan.FromSeconds overflows. If this ever stops throwing, GetVideoThumbnailCount is no
    // longer the only thing keeping Infinity out of the thumbnail time math.
    [Test]
    public void FromSeconds_OfInfinity_ThrowsOverflow()
    {
        // duration.TotalSeconds / count when count == 0 yields +Infinity.
        Assert.Throws<OverflowException>(() => TimeSpan.FromSeconds(double.PositiveInfinity * 0.5));
    }

    [Test]
    [NonParallelizable]
    public async Task ThumbnailRenderResources_AreDisposedOnRenderThread()
    {
        var resources = new[]
        {
            new DisposalThreadProbe(),
            new DisposalThreadProbe(),
            new DisposalThreadProbe(),
        };

        Assert.That(RenderThread.Dispatcher.CheckAccess(), Is.False,
            "the fixture must begin on the thumbnail consumer's non-render thread");
        await SourceVideo.DisposeThumbnailRenderResourcesAsync(resources);

        Assert.That(resources, Has.All.Matches<DisposalThreadProbe>(static item =>
            item.DisposeCount == 1 && item.DisposedOnRenderThread));
    }

    private sealed class DisposalThreadProbe : IDisposable
    {
        public int DisposeCount { get; private set; }

        public bool DisposedOnRenderThread { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            DisposedOnRenderThread = RenderThread.Dispatcher.CheckAccess();
        }
    }
}
