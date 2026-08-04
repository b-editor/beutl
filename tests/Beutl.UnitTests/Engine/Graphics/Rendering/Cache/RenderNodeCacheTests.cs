using System.Linq;
using System.Runtime.CompilerServices;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class RenderNodeCacheTests
{
    [Test]
    [TestCase(3)]
    [TestCase(4)]
    public void ReportRenderCount_GreaterThanOrEqualToThree_ShouldSetCanCacheToTrue(int count)
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act
        cache.ReportRenderCount(count);

        // Assert
        Assert.That(cache.CanCache(), Is.True);
    }

    [Test]
    public void IncrementRenderCount_CalledThreeOrMoreTimes_ShouldSetCanCacheToTrue()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act
        cache.IncrementRenderCount();
        cache.IncrementRenderCount();
        cache.IncrementRenderCount();

        // Assert
        Assert.That(cache.CanCache(), Is.True);
    }

    [Test]
    public void IncrementRenderCount_WhenNodeChanged_ShouldInvalidateExistingCache()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        Rect bounds = new(0, 0, 1, 1);
        RenderNodeCache.PublishAtomically(
            [RenderCacheTestSupport.CreatePublication(node.Cache, renderTarget, bounds)]);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        node.HasChanges = true;

        // Act
        node.Cache.IncrementRenderCount();

        // Assert
        Assert.That(node.Cache.IsCached, Is.False);
        Assert.That(node.Cache.CanCache(), Is.False);
    }

    [Test]
    [NonParallelizable]
    public void Finalizer_SwallowsCachedTargetCleanupFailure()
    {
        var cleanup = new InvalidOperationException("finalizer cache cleanup failed");
        var target = new ThrowingRenderTarget(cleanup);
        WeakReference cacheReference = CreateAbandonedCache(target);

        for (int attempt = 0; attempt < 3 && cacheReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.Multiple(() =>
        {
            Assert.That(cacheReference.IsAlive, Is.False);
            Assert.That(target.IsDisposed, Is.True);
            Assert.That(target.DisposeCalls, Is.EqualTo(1));
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAbandonedCache(ThrowingRenderTarget target)
    {
        var node = new ContainerRenderNode();
        var cache = new RenderNodeCache(node);
        Rect bounds = new(0, 0, 1, 1);
        var fragment = new RenderFragmentReference(
            RenderFragmentKind.Layer,
            bounds,
            EffectiveScale.At(1),
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            inputs: null,
            payload: null,
            hitTest: null);
        var identity = new RenderOutputCacheIdentity(
            "finalizer-cache",
            RenderFragmentOutputIdentity.Create(fragment, new RenderRequestId(1)),
            bounds,
            RequiredRegion.Region(bounds),
            density: 1,
            RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            FusionMode.Enabled,
            new RenderCacheDeviceContextIdentity("finalizer-device", "finalizer-context"));
        RenderNodeCache.PublishAtomically(
        [
            new RenderNodeCachePublication(
                cache,
                identity,
                [new RenderNodeCachedValue(target, bounds, EffectiveScale.At(1))]),
        ]);
        return new WeakReference(cache);
    }

    private sealed class ThrowingRenderTarget(Exception failure)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                1,
                1,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            1,
            1)
    {
        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            bool fail = disposing && !IsDisposed;
            if (fail)
                DisposeCalls++;
            base.Dispose(disposing);
            if (fail)
                throw failure;
        }
    }
}
