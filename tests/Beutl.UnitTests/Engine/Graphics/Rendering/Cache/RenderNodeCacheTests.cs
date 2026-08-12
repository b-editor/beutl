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
    public void StableRequests_ReachingThreshold_AdmitsCacheCapture()
    {
        using var node = new ContainerRenderNode();

        for (int i = 0; i < RenderNodeCache.StableRequestCount; i++)
        {
            node.Cache.RecordSuccessfulStableRequest();
        }

        Assert.That(node.Cache.CanCapture, Is.True);
    }

    [Test]
    public void DirtyNode_BeginLifecycleInvalidatesCacheAndResetsWarmup()
    {
        using var node = new ContainerRenderNode();
        RenderNodeCache.PublishAtomically(
            [RenderCacheTestSupport.CreatePublication(node.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1))]);
        for (int i = 0; i < RenderNodeCache.StableRequestCount; i++)
        {
            node.Cache.RecordSuccessfulStableRequest();
        }
        node.HasChanges = true;

        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node);

        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(node.Cache.SuccessfulStableRequestCount, Is.Zero);
            Assert.That(node.HasChanges, Is.True);
        });

        lifecycle.CompleteSuccessfully(advanceWarmup: true);

        Assert.That(node.HasChanges, Is.False);
    }

    [Test]
    public void DirtyBranch_BeginLifecycleInvalidatesItselfAndAncestorsButKeepsUnchangedDescendantsAndSiblings()
    {
        using var root = new ContainerRenderNode();
        using var dirtyBranch = new ContainerRenderNode();
        using var dirtyLeaf = new ContainerRenderNode();
        using var sibling = new ContainerRenderNode();
        root.AddChild(dirtyBranch);
        root.AddChild(sibling);
        dirtyBranch.AddChild(dirtyLeaf);

        RenderNodeCache.PublishAtomically(
        [
            RenderCacheTestSupport.CreatePublication(root.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1)),
            RenderCacheTestSupport.CreatePublication(dirtyBranch.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1)),
            RenderCacheTestSupport.CreatePublication(dirtyLeaf.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1)),
            RenderCacheTestSupport.CreatePublication(sibling.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1)),
        ]);
        for (int i = 0; i < RenderNodeCache.StableRequestCount; i++)
        {
            root.Cache.RecordSuccessfulStableRequest();
            dirtyBranch.Cache.RecordSuccessfulStableRequest();
            dirtyLeaf.Cache.RecordSuccessfulStableRequest();
            sibling.Cache.RecordSuccessfulStableRequest();
        }
        dirtyBranch.HasChanges = true;

        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(root);

        Assert.Multiple(() =>
        {
            Assert.That(root.Cache.IsCached, Is.False, "a dirty descendant must invalidate its ancestor");
            Assert.That(dirtyBranch.Cache.IsCached, Is.False);
            Assert.That(root.Cache.SuccessfulStableRequestCount, Is.Zero);
            Assert.That(dirtyBranch.Cache.SuccessfulStableRequestCount, Is.Zero);
            Assert.That(dirtyLeaf.Cache.IsCached, Is.True, "an unchanged descendant remains reusable");
            Assert.That(dirtyLeaf.Cache.CanCapture, Is.True, "an unchanged descendant retains its warm-up");
            Assert.That(sibling.Cache.IsCached, Is.True, "an unrelated sibling remains reusable");
            Assert.That(sibling.Cache.CanCapture, Is.True, "an unrelated sibling retains its warm-up");
        });

        lifecycle.CompleteSuccessfully(advanceWarmup: true);

        Assert.That(dirtyBranch.HasChanges, Is.False);
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
