using System.Linq;
using System.Runtime.CompilerServices;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class RenderNodeCacheTests
{
    [Test]
    public void Reset_StopsReleasingTargetsAfterTheFirstCleanupFailure()
    {
        using var node = new ContainerRenderNode();
        using var remaining = new CpuRenderTarget(1, 1);
        var failure = new IOException("cache cleanup failed");
        using var faulting = new FailureTestRenderTarget(new PixelSize(1, 1), failure);
        var bounds = new Rect(0, 0, 1, 1);
        RenderNodeCachePublication publication = RenderCacheTestSupport.CreatePublication(node.Cache, remaining, bounds);
        RenderNodeCachedValue failedValue = RenderCacheTestSupport.CreatePublication(node.Cache, faulting, bounds).Values[0];
        RenderNodeCache.PublishAtomically([publication with { Values = [.. publication.Values, failedValue] }]);

        Assert.That(Assert.Throws<IOException>(node.Cache.Reset), Is.SameAs(failure));
        Assert.That(node.Cache.IsCached, Is.False);
        Assert.That(faulting.IsDisposed, Is.True);
        Assert.That(remaining.IsDisposed, Is.False);
    }

    [Test]
    public void PublicationCleanupFailure_LeavesLaterOldCachesAloneAndKeepsTheNewTargets()
    {
        using var first = new ContainerRenderNode();
        using var second = new ContainerRenderNode();
        using var remaining = new CpuRenderTarget(1, 1);
        var failure = new IOException("old cache cleanup failed");
        using var faulting = new FailureTestRenderTarget(new PixelSize(1, 1), failure);
        using var firstReplacement = new CpuRenderTarget(1, 1);
        using var secondReplacement = new CpuRenderTarget(1, 1);
        var bounds = new Rect(0, 0, 1, 1);
        RenderNodeCache.PublishAtomically([
            RenderCacheTestSupport.CreatePublication(first.Cache, remaining, bounds),
            RenderCacheTestSupport.CreatePublication(second.Cache, faulting, bounds)]);

        IReadOnlyList<Exception> failures = RenderNodeCache.PublishAtomically([
            RenderCacheTestSupport.CreatePublication(first.Cache, firstReplacement, bounds),
            RenderCacheTestSupport.CreatePublication(second.Cache, secondReplacement, bounds)]);

        Assert.That(failures, Has.Exactly(1).SameAs(failure));
        Assert.That(faulting.IsDisposed, Is.True);
        Assert.That(remaining.IsDisposed, Is.False);
        Assert.That(firstReplacement.IsDisposed, Is.False);
        Assert.That(secondReplacement.IsDisposed, Is.False);
        first.Cache.TryGetCachedOutput(first.Cache.CurrentCacheIdentity(), out RenderNodeCachedOutput? firstOutput);
        second.Cache.TryGetCachedOutput(second.Cache.CurrentCacheIdentity(), out RenderNodeCachedOutput? secondOutput);
        Assert.That(firstOutput?.Values.Single().Target, Is.SameAs(firstReplacement));
        Assert.That(secondOutput?.Values.Single().Target, Is.SameAs(secondReplacement));
    }

    [Test]
    public void StableRequests_ReachingThreshold_AdmitsCacheCapture()
    {
        using var node = new ContainerRenderNode();

        node.Cache.RecordStableRequests();

        Assert.That(node.Cache.CanCapture, Is.True);
    }

    [Test]
    public void DirtyNode_BeginLifecycleInvalidatesCacheAndResetsWarmup()
    {
        using var node = new ContainerRenderNode();
        RenderNodeCache.PublishAtomically(
            [RenderCacheTestSupport.CreatePublication(node.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1))]);
        node.Cache.RecordStableRequests();
        node.MarkChanged();

        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheLifecycle.Create(node);

        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(node.Cache.CanCapture, Is.False);
            Assert.That(node.HasChanges, Is.True);
        });
        node.Cache.AssertWarmupRestarted();

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
        root.Cache.RecordStableRequests();
        dirtyBranch.Cache.RecordStableRequests();
        dirtyLeaf.Cache.RecordStableRequests();
        sibling.Cache.RecordStableRequests();
        dirtyBranch.MarkChanged();

        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheLifecycle.Create(root);

        Assert.Multiple(() =>
        {
            Assert.That(root.Cache.IsCached, Is.False, "a dirty descendant must invalidate its ancestor");
            Assert.That(dirtyBranch.Cache.IsCached, Is.False);
            Assert.That(root.Cache.CanCapture, Is.False);
            Assert.That(dirtyBranch.Cache.CanCapture, Is.False);
            Assert.That(dirtyLeaf.Cache.IsCached, Is.True, "an unchanged descendant remains reusable");
            Assert.That(dirtyLeaf.Cache.CanCapture, Is.True, "an unchanged descendant retains its warm-up");
            Assert.That(sibling.Cache.IsCached, Is.True, "an unrelated sibling remains reusable");
            Assert.That(sibling.Cache.CanCapture, Is.True, "an unrelated sibling retains its warm-up");
        });
        root.Cache.AssertWarmupRestarted();
        dirtyBranch.Cache.AssertWarmupRestarted();

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
            inputs: [],
            payload: null,
            hitTest: RenderFragmentHitTest.None);
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
                [new RenderNodeCachedValue(target, bounds, EffectiveScale.At(1), PixelRect.FromRect(bounds, 1))]),
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
