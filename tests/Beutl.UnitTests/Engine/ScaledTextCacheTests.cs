using Beutl.Graphics;
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
    private (SKTextBlob? TextBlob, SKPath? StrokePath, Rect RasterBounds) CreateScaledText(float density)
    {
        return (SKTextBlob.Create("A", _font), new SKPath(), new Rect(0, 0, density, density));
    }

    [Test]
    public void Get_EvictsTheLeastRecentlyUsedDensities_WhenExceedingMaxEntries()
    {
        using var cache = new ScaledTextCache(CreateScaledText);

        // More distinct densities than the cache cap (8) to drive eviction.
        var blobs = new List<SKTextBlob>();
        for (int i = 1; i <= 12; i++)
        {
            (SKTextBlob? blob, _, _) = cache.Get(1f + i * 0.25f);
            Assert.That(blob, Is.Not.Null, $"density #{i} should produce a scaled blob");
            blobs.Add(blob!);
        }

        Assert.Multiple(() =>
        {
            // Eviction disposes the four oldest entries and keeps the eight most recent ones live.
            for (int i = 0; i < blobs.Count; i++)
                Assert.That(blobs[i].Handle == IntPtr.Zero, Is.EqualTo(i < 4), $"density #{i + 1}");
            Assert.That(cache.Get(1f + 12 * 0.25f).TextBlob, Is.SameAs(blobs[^1]));
        });
    }
}
