using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class ContainerTopologyCacheInvalidationTests
{
    [Test]
    public void EveryMutator_ReportsTheTopologyChange()
    {
        using var node = new ContainerRenderNode();
        var first = new ContainerRenderNode();
        var second = new ContainerRenderNode();

        node.AddChild(first);
        Assert.That(node.HasChanges, Is.True, "AddChild changes what the container composes.");

        Settle(node);
        node.SetChild(0, second);
        Assert.That(node.HasChanges, Is.True, "SetChild changes what the container composes.");

        Settle(node);
        node.RemoveChild(second);
        Assert.That(node.HasChanges, Is.True, "RemoveChild changes what the container composes.");

        node.AddChild(new ContainerRenderNode());
        Settle(node);
        node.RemoveRange(0, 1);
        Assert.That(node.HasChanges, Is.True, "RemoveRange changes what the container composes.");
    }

    [Test]
    public void RemovingNothing_IsNotAChange()
    {
        using var node = new ContainerRenderNode();
        using var absent = new ContainerRenderNode();
        node.AddChild(new ContainerRenderNode());
        Settle(node);

        node.RemoveChild(absent);
        node.RemoveRange(0, 0);

        Assert.That(node.HasChanges, Is.False);
    }

    [Test]
    public void SetChild_WithTheChildAlreadyThere_IsANoOp()
    {
        using var node = new ContainerRenderNode();
        var child = new ContainerRenderNode();
        node.AddChild(child);
        Settle(node);

        node.SetChild(0, child);

        Assert.Multiple(() =>
        {
            Assert.That(child.IsDisposed, Is.False, "Self-replacement must not dispose the child it stored.");
            Assert.That(node.Children[0], Is.SameAs(child));
            Assert.That(node.HasChanges, Is.False, "Nothing changed, so nothing needs re-rendering.");
        });
    }

    [Test]
    public void BringFrom_ReportsTheTopologyChangeOnBothContainers()
    {
        using var destination = new ContainerRenderNode();
        using var source = new ContainerRenderNode();
        source.AddChild(new ContainerRenderNode());
        Settle(destination);
        Settle(source);

        destination.BringFrom(source);

        Assert.Multiple(() =>
        {
            Assert.That(destination.HasChanges, Is.True);
            Assert.That(source.HasChanges, Is.True);
        });
    }

    [Test]
    public void ReplacingAChild_InvalidatesTheContainerCache()
    {
        using var node = new ContainerRenderNode();
        node.AddChild(new ContainerRenderNode());
        PublishAndSettle(node);
        Assert.That(node.Cache.IsCached, Is.True, "precondition: the container starts with a warm cache");

        node.SetChild(0, new ContainerRenderNode());
        RenderNodeCacheHelper.BeginLifecycle(node);

        Assert.That(node.Cache.IsCached, Is.False);
    }

    private static void Settle(ContainerRenderNode node)
        => RenderNodeCacheHelper.BeginLifecycle(node).CompleteSuccessfully(advanceWarmup: false);

    private static void PublishAndSettle(RenderNode node)
    {
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node);
        using (var target = RenderTarget.CreateNull(1, 1))
        {
            RenderNodeCache.PublishAtomically(
                [RenderCacheTestSupport.CreatePublication(node.Cache, target, new Rect(0, 0, 1, 1))]);
        }

        lifecycle.CompleteSuccessfully(advanceWarmup: false);
    }
}
