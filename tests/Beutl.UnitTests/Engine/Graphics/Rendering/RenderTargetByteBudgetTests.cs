using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Language;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderTargetByteBudgetTests
{
    private const long TargetBytes = 8 * 8 * 8; // RGBA16F
    private static readonly PixelSize s_size = new(8, 8);
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void ATargetFitsAloneButCannotBeLeasedWhileAnotherIsLive()
    {
        var factory = new CpuRenderTargetFactory();
        using var pool = new RenderTargetPool(factory, new() { MaximumLiveBytes = TargetBytes });
        var smaller = new PixelSize(4, 8);
        using (RenderTargetLeaseSession warmup = pool.BeginSession(RenderIntent.Delivery))
        using (warmup.Acquire(smaller))
        {
        }

        using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Preview);
        using RenderTargetLease first = session.Acquire(s_size);
        using RenderTargetLease? refusedReuse = session.TryAcquire(smaller);
        using RenderTargetLease? refusedCreate = session.TryAcquire(s_size);
        Assert.Multiple(() =>
        {
            Assert.That(refusedReuse, Is.Null);
            Assert.That(refusedCreate, Is.Null);
            Assert.That(factory.AllocationCount, Is.EqualTo(2));
            Assert.That(pool.Statistics.AvailableTargets, Is.EqualTo(1));
            Assert.That(session.LiveBytes, Is.EqualTo(TargetBytes));
        });

        first.Dispose();
        using RenderTargetLease reused = session.Acquire(smaller);
        Assert.Multiple(() =>
        {
            Assert.That(pool.Statistics.Reuses, Is.EqualTo(1));
            Assert.That(pool.Statistics.PeakLiveBytes, Is.EqualTo(TargetBytes));
            Assert.That(session.LiveBytes, Is.EqualTo(TargetBytes / 2));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedEffectReplacementIsCountedOnceUntilItsLastReferenceReleases(bool useFactory)
    {
        using var pool = new RenderTargetPool(
            useFactory ? new CpuRenderTargetFactory() : null,
            new() { MaximumLiveBytes = 2 * TargetBytes });
        using RenderTargetLeaseSession outer = pool.BeginSession(RenderIntent.Preview);
        using var targets = new EffectTargets();
        var nested = new CustomFilterEffectContext(targets, new(RenderIntent.Preview, RenderRequestPurpose.Frame)
        {
            RenderTargetLeaseSession = outer,
        });

        using EffectTarget source = EffectTarget.FromLease(
            outer.Acquire(s_size), s_bounds, EffectiveScale.At(1), new PixelRect(default, s_size));
        using EffectTarget replacement = nested.CreateTargetLike(source);
        Assert.That(replacement.IsEmpty, Is.False);
        using EffectTarget copy = replacement.Clone();
        replacement.Dispose();

        using RenderTargetLease? refused = outer.TryAcquire(s_size);
        using EffectTarget refusedReplacement = nested.CreateTargetLike(source);
        Assert.Multiple(() =>
        {
            Assert.That(copy.IsEmpty, Is.False);
            Assert.That(outer.LiveBytes, Is.EqualTo(2 * TargetBytes));
            Assert.That(refused, Is.Null);
            Assert.That(refusedReplacement.IsEmpty, Is.True);
        });

        copy.Dispose();
        Assert.That(outer.LiveBytes, Is.EqualTo(TargetBytes));
        using EffectTarget nextReplacement = nested.CreateTargetLike(source);
        Assert.That(nextReplacement.IsEmpty, Is.False);
    }

    [Test]
    public void AllocatorRefusalReturnsItsReservation()
    {
        using var factory = new FailureTestTargetFactory(failAt: 0);
        using var pool = new RenderTargetPool(factory, new() { MaximumLiveBytes = TargetBytes });
        using RenderTargetLeaseSession session = pool.BeginSession(RenderIntent.Preview);

        using RenderTargetLease? refused = session.TryAcquire(s_size);
        Assert.That(refused, Is.Null);
        Assert.That(session.LiveBytes, Is.Zero);

        using RenderTargetLease next = session.Acquire(s_size);
        Assert.That(session.LiveBytes, Is.EqualTo(TargetBytes));
    }

    [Test]
    public void CompletingAnOldDeferredLeaseDoesNotChangeTheNextRequestsBudget()
    {
        using var pool = new RenderTargetPool(new CpuRenderTargetFactory(), new() { MaximumLiveBytes = TargetBytes });
        using RenderTargetLeaseSession first = pool.BeginSession(RenderIntent.Delivery);
        RenderTargetLease pending = first.Acquire(s_size);
        pool.DeferRelease(pending);
        Assert.That(first.LiveBytes, Is.EqualTo(TargetBytes));
        first.Dispose();

        using RenderTargetLeaseSession next = pool.BeginSession(RenderIntent.Delivery);
        using RenderTargetLease current = next.Acquire(s_size);
        pool.CompleteDeferredRelease(pending);

        Assert.Multiple(() =>
        {
            Assert.That(first.LiveBytes, Is.Zero);
            Assert.That(next.LiveBytes, Is.EqualTo(TargetBytes));
        });
    }

    [TestCase(RenderIntent.Preview)]
    [TestCase(RenderIntent.Delivery)]
    public void NestedRequestRefusalFollowsTheIntentAndTheNextRenderRecovers(RenderIntent intent)
    {
        var nestedBounds = s_bounds;
        using var child = new RectangleRenderNode(s_bounds, Brushes.Resource.Red, null);
        var effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, (_, execution) =>
            {
                using RenderTargetImageLease image = execution.ExecutionContext!.RenderDependency(
                    child, "nested", nestedBounds);
            }));
        using var effectResource = effect.ToResource(CompositionContext.Default);
        var layer = new FilterEffectRenderNode(effectResource);
        layer.AddChild(new RectangleRenderNode(s_bounds, Brushes.Resource.Blue, null));
        using var root = new ContainerRenderNode();
        root.AddChild(layer);
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 2, 2), Brushes.Resource.Lime, null));

        // At the callback, the parent holds its 10 × 10 raster with apron and its flushed 8 × 8 input.
        const long parentBytes = 10 * 10 * 8 + TargetBytes;
        const long maximumLiveBytes = parentBytes + 8;
        var factory = new CpuRenderTargetFactory();
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = intent,
            TargetDomain = s_bounds,
            MaxWorkingScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, factory, programCaches: null, targetPoolOptions: new() { MaximumLiveBytes = maximumLiveBytes });
        using var destination = new CpuRenderTarget(s_size);
        using var canvas = new ImmediateCanvas(destination, intent);

        if (intent == RenderIntent.Delivery)
        {
            RenderTargetAllocationException? failure = Assert.Throws<RenderTargetAllocationException>(
                () => renderer.Render(canvas));
            Assert.That(failure!.Message, Is.EqualTo(string.Format(MessageStrings.RenderTargetLiveByteBudgetExceeded,
                s_size.Width, s_size.Height, maximumLiveBytes, parentBytes)));
        }
        else
        {
            renderer.Render(canvas);
            using RenderTargetImageLease image = destination.AcquireImage();
            using var pixels = SKBitmap.FromImage(image.Image);
            Assert.Multiple(() =>
            {
                Assert.That(pixels.GetPixel(1, 1), Is.EqualTo(SKColors.Lime));
                Assert.That(pixels.GetPixel(4, 4).Alpha, Is.Zero);
            });
        }
        Assert.That(factory.AllocationCount, Is.EqualTo(2));
        Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);

        nestedBounds = new Rect(0, 0, 1, 1);
        canvas.Clear();
        renderer.Render(canvas);
        using RenderTargetImageLease recovered = destination.AcquireImage();
        using var recoveredPixels = SKBitmap.FromImage(recovered.Image);
        Assert.Multiple(() =>
        {
            Assert.That(recoveredPixels.GetPixel(4, 4), Is.EqualTo(SKColors.Blue));
            Assert.That(renderer.TargetPoolStatistics.PeakLiveBytes, Is.EqualTo(maximumLiveBytes));
        });
    }
}
