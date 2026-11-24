using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Threading;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class SourceVideoThumbnailTests
{
    // Regression for Project #9 item #240: a degenerate frame size made the count 0, the interval +Infinity, and
    // TimeSpan.FromSeconds overflow. GetVideoThumbnailCount returns 0 so the caller bails out before that math.
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

    [Test]
    [NonParallelizable]
    public async Task GetThumbnailStrip_ReplacesCachedThumbnailWhenHeightChanges()
    {
        TestGpu.RequireAnyDevice();
        var decoders = TestMediaHelper.CreateRegistry();
        var video = new VideoSource();
        video.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(128, 64, new Rational(30, 1), 30)));
        var drawable = new SourceVideo
        {
            Source = { Value = video },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1)) },
        };
        var cache = new ThumbnailCacheService();
        string? baseKey = drawable.GetThumbnailsCacheKey();
        Assert.That(baseKey, Is.Not.Null);
        string key = baseKey + SourceVideo.OriginalThumbnailCacheKeySuffix;
        try
        {
            Assert.That(await ReadFirstThumbnailHeight(25), Is.EqualTo(25));
            Assert.That(GetCachedHeight(), Is.EqualTo(25));

            Assert.That(await ReadFirstThumbnailHeight(64), Is.EqualTo(64));
            Assert.That(GetCachedHeight(), Is.EqualTo(64), "The rerender replaces the entry under the same key.");
        }
        finally
        {
            SourceVideo.InvalidateThumbnailCacheKeys(cache, baseKey);
        }

        async Task<int> ReadFirstThumbnailHeight(int height)
        {
            await foreach (var (_, _, thumbnail) in drawable.GetThumbnailStripAsync(
                               TestFonts.Manager, maxWidth: 128, maxHeight: height, cacheService: cache,
                               endIndex: 0, decoders: decoders))
            {
                using (thumbnail)
                    return thumbnail.Height;
            }

            throw new AssertionException("The source did not yield a thumbnail.");
        }

        int GetCachedHeight()
        {
            Assert.That(cache.TryGet(key, MediaTime.Zero, MediaTime.Zero, out var bitmap), Is.True);
            using (bitmap)
                return bitmap!.Height;
        }
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
            item is { DisposeCount: 1, DisposedOnRenderThread: true }));
    }

    [Test]
    public async Task ThumbnailRenderResources_StopWaitingWhenDispatcherShutsDownBeforeQueuedCleanupRuns()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        using var blockerEntered = new ManualResetEventSlim();
        using var releaseBlocker = new ManualResetEventSlim();
        var resource = new DisposalThreadProbe();
        try
        {
            await dispatcher.InvokeAsync(static () => { });
            dispatcher.Dispatch(() =>
            {
                blockerEntered.Set();
                releaseBlocker.Wait();
            }, DispatchPriority.High);
            Assert.That(blockerEntered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "The dispatcher fixture did not enter its blocking operation.");

            Task cleanup = SourceVideo.DisposeThumbnailRenderResourcesAsync(dispatcher, resource);
            dispatcher.Shutdown();

            await cleanup.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(resource.DisposeCount, Is.Zero,
                "Thread-affine resources must not be disposed from the thumbnail consumer after shutdown.");
        }
        finally
        {
            releaseBlocker.Set();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True,
                "The local dispatcher did not finish after its blocker was released.");
        }
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
