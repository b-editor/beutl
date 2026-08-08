using System.Collections.Immutable;
using System.Reactive.Linq;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Models;

using SkiaSharp;

namespace Beutl.HeadlessUITests;

/// <summary>
/// The timeline paints cache blocks purely from <see cref="FrameCacheManager.BlocksUpdated"/>, and the
/// preview shows whatever an entry holds, so eviction and option changes both have to be observable.
/// </summary>
[TestFixture]
public class FrameCacheManagerTests
{
    private const int Side = 64;
    private static readonly PixelSize FrameSize = new(Side, Side);
    private const long EntryBytes = Side * Side * 4;

    private static FrameCacheManager NewManager(
        FrameCacheOptions options, long maxSizeBytes = long.MaxValue)
    {
        return new FrameCacheManager(FrameSize, options, Observable.Return(maxSizeBytes))
        {
            IsEnabled = true
        };
    }

    private static Bitmap CreateFrame()
    {
        var info = new SKImageInfo(Side, Side, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var skBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(skBitmap))
        {
            canvas.Clear(SKColors.Gray);
        }

        return new Bitmap(skBitmap);
    }

    private static void Add(FrameCacheManager manager, int frame)
    {
        using Ref<Bitmap> source = Ref<Bitmap>.Create(CreateFrame());
        manager.Add(frame, source);
    }

    private static bool Contains(FrameCacheManager manager, int frame)
    {
        if (!manager.TryGet(frame, out Ref<Bitmap>? cached))
        {
            return false;
        }

        cached.Dispose();
        return true;
    }

    private static int TotalCachedFrames(ImmutableArray<FrameCacheManager.CacheBlock> blocks)
    {
        return blocks.Sum(b => b.Length);
    }

    [Test]
    public void Eviction_ReportsTheReducedBlocksToTheTimeline()
    {
        using FrameCacheManager manager = NewManager(
            new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA),
            maxSizeBytes: EntryBytes * 4);

        const int AddedFrames = 24;
        using var evicted = new ManualResetEventSlim();
        ImmutableArray<FrameCacheManager.CacheBlock> reported = [];
        manager.BlocksUpdated += blocks =>
        {
            // Raised under the manager's lock, so handlers never overlap; keep the first payload
            // to pair with the Set/Wait barrier below.
            if (evicted.IsSet) return;

            reported = blocks;
            evicted.Set();
        };

        for (int i = 0; i < AddedFrames; i++)
        {
            Add(manager, i);
        }

        Assert.That(evicted.Wait(TimeSpan.FromSeconds(10)), Is.True,
            "eviction must report the surviving blocks; the timeline has no other source for them.");
        Assert.That(TotalCachedFrames(reported), Is.LessThan(AddedFrames));
    }

    [Test]
    public void ChangingOnlyTheDeletionStrategy_KeepsTheEntries()
    {
        using FrameCacheManager manager = NewManager(new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        Add(manager, 0);

        // Playback flips this on every start/stop; dropping the cache there would defeat the feature.
        manager.Options = manager.Options with { DeletionStrategy = FrameCacheDeletionStrategy.BackwardBlock };

        Assert.That(Contains(manager, 0), Is.True);
    }

    [TestCase(FrameCacheScale.Half, null)]
    [TestCase(FrameCacheScale.Manual, Side / 2)]
    public void ChangingTheStoredResolution_DropsTheEntries(FrameCacheScale scale, int? manualSide)
    {
        using FrameCacheManager manager = NewManager(new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        Add(manager, 0);
        Assert.That(Contains(manager, 0), Is.True);

        manager.Options = manager.Options with
        {
            Scale = scale,
            Size = manualSide is { } side ? new PixelSize(side, side) : null
        };

        Assert.That(Contains(manager, 0), Is.False,
            "entries encoded at the previous resolution must not be mixed with new ones.");
    }

    [Test]
    public void ChangingTheColorType_DropsTheEntries()
    {
        using FrameCacheManager manager = NewManager(new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        Add(manager, 0);

        manager.Options = manager.Options with { ColorType = FrameCacheColorType.YUV };

        Assert.That(Contains(manager, 0), Is.False);
    }

    /// <summary>
    /// A rendered frame is premultiplied; the preview draws the cache entry through the same code
    /// path as a fresh snapshot, so the entry has to describe its own pixels the same way or
    /// translucent areas change appearance the moment a frame comes from the cache.
    /// </summary>
    [Test]
    public void RoundTrip_TranslucentFrame_PreservesTheRenderedColors()
    {
        var info = new SKImageInfo(Side, Side, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var skBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(skBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawRect(new SKRect(0, 0, Side, Side),
                new SKPaint { Color = new SKColor(255, 255, 255, 128), BlendMode = SKBlendMode.Src });
        }

        using var source = new Bitmap(skBitmap);
        using Bitmap expected = source.Convert(BitmapColorType.Bgra8888, colorSpace: BitmapColorSpace.Srgb);

        using FrameCacheManager manager = NewManager(new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        using (Ref<Bitmap> reference = Ref<Bitmap>.Create(source.Clone()))
        {
            manager.Add(0, reference);
        }

        Assert.That(manager.TryGet(0, out Ref<Bitmap>? cached), Is.True);
        using (cached)
        {
            Bitmap actual = cached!.Value;
            Assert.That(actual.AlphaType, Is.EqualTo(expected.AlphaType));

            ReadOnlySpan<Bgra8888> expectedPixels = expected.GetPixelSpan<Bgra8888>();
            ReadOnlySpan<Bgra8888> actualPixels = actual.GetPixelSpan<Bgra8888>();
            Bgra8888 e = expectedPixels[Side / 2 * Side + Side / 2];
            Bgra8888 a = actualPixels[Side / 2 * Side + Side / 2];

            Assert.Multiple(() =>
            {
                Assert.That((int)a.A, Is.EqualTo((int)e.A).Within(2), "alpha");
                Assert.That((int)a.R, Is.EqualTo((int)e.R).Within(2), "red");
                Assert.That((int)a.G, Is.EqualTo((int)e.G).Within(2), "green");
                Assert.That((int)a.B, Is.EqualTo((int)e.B).Within(2), "blue");
            });
        }
    }

    [Test]
    public void RoundTrip_UnpremultipliedNativeFrame_PreservesTheRenderedColors()
    {
        // Already BGRA/sRGB, so nothing but the alpha type can send this through the conversion path.
        using var source = new Bitmap(Side, Side, BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul);
        var translucent = new Bgra8888(40, 80, 200, 128);
        source.GetPixelSpan<Bgra8888>().Fill(translucent);

        using Bitmap expected = source.Convert(
            BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.Srgb);

        using FrameCacheManager manager = NewManager(
            new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        using (Ref<Bitmap> reference = Ref<Bitmap>.Create(source.Clone()))
        {
            manager.Add(0, reference);
        }

        Assert.That(manager.TryGet(0, out Ref<Bitmap>? cached), Is.True);
        using (cached)
        {
            Bgra8888 e = expected.GetPixelSpan<Bgra8888>()[Side / 2 * Side + Side / 2];
            Bgra8888 a = cached!.Value.GetPixelSpan<Bgra8888>()[Side / 2 * Side + Side / 2];

            Assert.Multiple(() =>
            {
                Assert.That((int)a.A, Is.EqualTo((int)e.A).Within(2), "alpha");
                Assert.That((int)a.R, Is.EqualTo((int)e.R).Within(2), "red");
                Assert.That((int)a.G, Is.EqualTo((int)e.G).Within(2), "green");
                Assert.That((int)a.B, Is.EqualTo((int)e.B).Within(2), "blue");
            });
        }
    }

    [Test]
    public void AnEntryThatExactlyFitsTheBudget_IsKept()
    {
        using FrameCacheManager manager = NewManager(
            new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA), EntryBytes);

        Add(manager, 0);
        Thread.Sleep(200);

        Assert.That(Contains(manager, 0), Is.True);
    }

    [Test]
    public void ReRenderingALockedFrame_RefreshesThePicture()
    {
        using FrameCacheManager manager = NewManager(
            new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        Add(manager, 0);
        manager.Lock(0, 1);

        using (var replacement = new Bitmap(Side, Side, BitmapColorType.Bgra8888, BitmapAlphaType.Premul))
        {
            replacement.GetPixelSpan<Bgra8888>().Fill(new Bgra8888(200, 20, 30, 255));
            using Ref<Bitmap> reference = Ref<Bitmap>.Create(replacement.Clone());
            manager.Add(0, reference);
        }

        Assert.That(manager.TryGet(0, out Ref<Bitmap>? cached), Is.True);
        using (cached)
        {
            Bgra8888 pixel = cached!.Value.GetPixelSpan<Bgra8888>()[Side / 2 * Side + Side / 2];
            Assert.That((int)pixel.R, Is.EqualTo(200).Within(2),
                "a locked entry is pinned against deletion, not frozen");
        }
    }

    [Test]
    public void ReassigningTheSameOptions_KeepsTheEntries()
    {
        using FrameCacheManager manager = NewManager(new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        Add(manager, 0);

        manager.Options = manager.Options with { };

        Assert.That(Contains(manager, 0), Is.True);
    }

    [Test]
    public void SwitchingScaleMode_DropsTheEntries_EvenWhenTheLogicalSizesAgree()
    {
        using FrameCacheManager manager = NewManager(new FrameCacheOptions(FrameCacheScale.Original, FrameCacheColorType.BGRA));

        Add(manager, 0);

        // Manual with the frame's own size resolves to the same logical size as Original, but an
        // entry is encoded from the rendered snapshot, whose size is the device size.
        manager.Options = manager.Options with { Scale = FrameCacheScale.Manual, Size = FrameSize };

        Assert.That(Contains(manager, 0), Is.False);
    }
}
