using System.Linq;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class RenderNodeCacheTests
{
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
}
