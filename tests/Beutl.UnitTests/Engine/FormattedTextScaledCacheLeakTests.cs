using Beutl.Composition;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class FormattedTextScaledCacheLeakTests
{
    [SetUp]
    public void Setup()
    {
        // Log.LoggerFactory is write-once (??=); skip allocating a factory we would only discard
        // when another fixture already set one.
        if (Log.LoggerFactory is null)
        {
            Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        }

        _ = TypefaceProvider.Typeface();
    }

    private static FormattedText CreateText(string text = "ABC", float size = 24f, bool withPen = false)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var ft = new FormattedText
        {
            Font = typeface.FontFamily,
            Style = typeface.Style,
            Weight = typeface.Weight,
            Size = size,
            Spacing = 1f,
            Text = text
        };

        if (withPen)
        {
            ft.Pen = new Pen
            {
                Thickness = { CurrentValue = 2f },
                Brush = { CurrentValue = Brushes.White }
            }.ToResource(CompositionContext.Default);
        }

        return ft;
    }

    [Test]
    public void GetScaledTextCache_DisposesUncommittedHandlesAndPropagates_WhenCommitThrows()
    {
        using FormattedText ft = CreateText(withPen: true);
        _ = ft.ActualBounds; // measure the base (density 1) first

        SKTextBlob? capturedBlob = null;
        SKPath? capturedStroke = null;
        ft._scaledTextCacheCommitFaultHook = (SKTextBlob? blob, SKPath? stroke) =>
        {
            capturedBlob = blob;
            capturedStroke = stroke;
            throw new InvalidOperationException("commit-fault");
        };

        var ex = Assert.Throws<InvalidOperationException>(() => ft.GetTextBlob(2f));

        Assert.That(ex!.Message, Is.EqualTo("commit-fault"),
            "the original cache-insert failure must propagate unchanged");
        Assert.That(capturedBlob, Is.Not.Null, "the scaled blob should have been allocated before the fault");
        Assert.That(capturedBlob!.Handle, Is.EqualTo(IntPtr.Zero),
            "the uncommitted scaled textBlob must be disposed when the cache insert fails");
        Assert.That(capturedStroke, Is.Not.Null, "a Pen was set, so a scaled strokePath should exist");
        Assert.That(capturedStroke!.Handle, Is.EqualTo(IntPtr.Zero),
            "the uncommitted scaled strokePath must be disposed when the cache insert fails");
    }

    [Test]
    public void GetScaledTextCache_StaysConsistent_AfterCommitFailure()
    {
        using FormattedText ft = CreateText(withPen: true);
        _ = ft.ActualBounds;

        ft._scaledTextCacheCommitFaultHook = (SKTextBlob? _, SKPath? _) => throw new InvalidOperationException("commit-fault");
        Assert.Throws<InvalidOperationException>(() => ft.GetTextBlob(2f));

        // The failed insert must roll back the LRU node it speculatively added; otherwise a phantom
        // node leaks and the LRU list drifts out of sync with the cache dictionary. Re-access alone
        // can't catch this (the dictionary self-heals on the next eviction), so assert the invariant.
        (int cacheCount, int lruCount) = ft.ScaledTextCacheCounts;
        Assert.That(cacheCount, Is.EqualTo(0), "a failed commit must not leave a cache entry");
        Assert.That(lruCount, Is.EqualTo(cacheCount),
            "the LRU list must stay in lockstep with the cache dictionary after a failed commit");

        // A later access at the same density still succeeds and produces a fresh, live blob.
        ft._scaledTextCacheCommitFaultHook = null;
        SKTextBlob? blob = ft.GetTextBlob(2f);
        Assert.That(blob, Is.Not.Null);
        Assert.That(blob!.Handle, Is.Not.EqualTo(IntPtr.Zero));
    }

    [Test]
    public void GetScaledTextCache_EvictsWithoutCorruption_WhenExceedingMaxEntries()
    {
        using FormattedText ft = CreateText();
        _ = ft.Bounds;

        // More distinct densities than the cache cap (8) to drive eviction; every fresh density
        // must still yield a live blob.
        for (int i = 1; i <= 12; i++)
        {
            float density = 1f + i * 0.25f;
            SKTextBlob? blob = ft.GetTextBlob(density);
            Assert.That(blob, Is.Not.Null, $"density {density} should produce a scaled blob");
            Assert.That(blob!.Handle, Is.Not.EqualTo(IntPtr.Zero));
        }

        // Eviction must keep the cache capped at MaxScaledTextCacheEntries (8) and the LRU list in
        // lockstep, even though 12 distinct densities were requested.
        (int cacheCount, int lruCount) = ft.ScaledTextCacheCounts;
        Assert.That(cacheCount, Is.EqualTo(8), "eviction must cap the cache at MaxScaledTextCacheEntries");
        Assert.That(lruCount, Is.EqualTo(cacheCount), "the LRU list must stay in lockstep with the cache dictionary");
    }
}
