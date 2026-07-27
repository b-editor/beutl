using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class ScaledTextCacheTests
{
    private SKFont _font = null!;

    [SetUp]
    public void Setup()
    {
        _font = new SKFont(TypefaceProvider.Typeface().ToSkia(), 24f);

        // The Handle assertions are only meaningful if the stub actually produces a live blob.
        using SKTextBlob? probe = SKTextBlob.Create("A", _font);
        Assert.That(probe, Is.Not.Null, "the test font must shape 'A'");
    }

    [TearDown]
    public void TearDown()
    {
        _font.Dispose();
    }

    // A fresh, live (blob, stroke) pair per density so disposing one entry can't disturb another;
    // both are real Skia handles so Handle == IntPtr.Zero observes actual native disposal.
    private (SKTextBlob? TextBlob, SKPath? StrokePath) CreateScaledText(float density)
    {
        return (SKTextBlob.Create("A", _font), new SKPath());
    }

    [Test]
    public void Get_DisposesUncommittedHandlesAndPropagates_WhenCommitThrows()
    {
        using var cache = new ScaledTextCache(CreateScaledText);

        SKTextBlob? capturedBlob = null;
        SKPath? capturedStroke = null;
        cache.CommitFaultHook = (SKTextBlob? blob, SKPath? stroke) =>
        {
            capturedBlob = blob;
            capturedStroke = stroke;
            throw new InvalidOperationException("commit-fault");
        };

        var ex = Assert.Throws<InvalidOperationException>(() => cache.Get(2f));

        Assert.That(ex!.Message, Is.EqualTo("commit-fault"),
            "the original cache-insert failure must propagate unchanged");
        Assert.That(capturedBlob, Is.Not.Null, "the scaled blob should have been produced before the fault");
        Assert.That(capturedBlob!.Handle, Is.EqualTo(IntPtr.Zero),
            "the uncommitted scaled textBlob must be disposed when the cache insert fails");
        Assert.That(capturedStroke, Is.Not.Null, "the stub supplies a strokePath");
        Assert.That(capturedStroke!.Handle, Is.EqualTo(IntPtr.Zero),
            "the uncommitted scaled strokePath must be disposed when the cache insert fails");
    }

    [Test]
    public void Get_StaysConsistent_AfterCommitFailure()
    {
        using var cache = new ScaledTextCache(CreateScaledText);

        cache.CommitFaultHook = (SKTextBlob? _, SKPath? _) => throw new InvalidOperationException("commit-fault");
        Assert.Throws<InvalidOperationException>(() => cache.Get(2f));

        // The failed insert must roll back the LRU node it speculatively added; otherwise a phantom
        // node leaks and the LRU list drifts out of sync with the cache dictionary.
        (int cacheCount, int lruCount) = cache.Counts;
        Assert.That(cacheCount, Is.EqualTo(0), "a failed commit must not leave a cache entry");
        Assert.That(lruCount, Is.EqualTo(cacheCount),
            "the LRU list must stay in lockstep with the cache dictionary after a failed commit");

        // A later access at the same density still succeeds and produces a fresh, live blob.
        cache.CommitFaultHook = null;
        (SKTextBlob? blob, _) = cache.Get(2f);
        Assert.That(blob, Is.Not.Null);
        Assert.That(blob!.Handle, Is.Not.EqualTo(IntPtr.Zero));
    }

    [Test]
    public void Get_EvictsWithoutCorruption_WhenExceedingMaxEntries()
    {
        using var cache = new ScaledTextCache(CreateScaledText);

        // More distinct densities than the cache cap (8) to drive eviction; every fresh density
        // must still yield a live blob.
        for (int i = 1; i <= 12; i++)
        {
            float density = 1f + i * 0.25f;
            (SKTextBlob? blob, _) = cache.Get(density);
            Assert.That(blob, Is.Not.Null, $"density {density} should produce a scaled blob");
            Assert.That(blob!.Handle, Is.Not.EqualTo(IntPtr.Zero));
        }

        // Eviction must keep the cache capped and the LRU list in lockstep, even though 12 distinct
        // densities were requested.
        (int cacheCount, int lruCount) = cache.Counts;
        Assert.That(cacheCount, Is.EqualTo(8), "eviction must cap the cache at the max entries");
        Assert.That(lruCount, Is.EqualTo(cacheCount), "the LRU list must stay in lockstep with the cache dictionary");
    }
}
