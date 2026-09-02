using System.Runtime.CompilerServices;

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

    [Test]
    public void TheDependencySignatureAloneCatchesAnUnreportedChildSwap()
    {
        using var node = new ContainerRenderNode();
        node.AddChild(new ContainerRenderNode());
        PublishAndSettle(node);

        node.SetChild(0, new ContainerRenderNode());
        // Stands in for any path that restructures the container without reporting it: both children were
        // built this instant, so nothing but the child's own identity distinguishes the two topologies.
        node.ClearChanges(node.ChangeVersion);
        Assert.That(node.HasChanges, Is.False, "precondition: the swap is reported by the signature alone");

        RenderNodeCacheHelper.BeginLifecycle(node);

        Assert.That(node.Cache.IsCached, Is.False);
    }

    [Test]
    public void AChildSwapTheRuntimeHashCannotTellApart_StillInvalidatesTheCache()
    {
        (ContainerRenderNode replaced, ContainerRenderNode replacement) = FindRuntimeHashCollidingPair();
        using var node = new SilentContainerRenderNode();
        node.SetChildren(replaced);
        PublishAndSettle(node);
        long stampedChangeVersion = node.ChangeVersion;

        node.SetChildren(replacement);

        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCached, Is.True, "precondition: the cache starts warm");
            Assert.That(node.HasChanges, Is.False, "precondition: the swap is not reported as a change");
            Assert.That(
                node.ChangeVersion, Is.EqualTo(stampedChangeVersion),
                "precondition: the change version must not distinguish the two topologies either");
            Assert.That(
                RuntimeHelpers.GetHashCode(replaced),
                Is.EqualTo(RuntimeHelpers.GetHashCode(replacement)),
                "precondition: the two children must be indistinguishable to a runtime hash code");
        });

        RenderNodeCacheHelper.BeginLifecycle(node);

        Assert.That(
            node.Cache.IsCached, Is.False,
            "a swap has to be caught by the child's identity, not by a hash the two children can share");

        replaced.Dispose();
        replacement.Dispose();
    }

    /// <summary>A container whose children change without the change being reported.</summary>
    /// <remarks>
    /// <see cref="ContainerRenderNode"/> marks every mutation, and that mark alone invalidates the cache, so
    /// it cannot show what the dependency signature contributes. This is the case the signature exists for:
    /// a node that rebuilds what it records through and cannot report it.
    /// </remarks>
    private sealed class SilentContainerRenderNode : RenderNode
    {
        private RenderNode[] _children = [];

        public override ReadOnlySpan<RenderNode> ChildNodes => _children;

        public void SetChildren(params RenderNode[] children) => _children = children;

        public override void Process(RenderNodeContext context)
        {
        }
    }

    /// <summary>Two render nodes that share a <see cref="RuntimeHelpers.GetHashCode(object)"/> value.</summary>
    /// <remarks>
    /// The value is drawn per object from a narrow range, so a colliding pair is found rather than
    /// constructed - and found within a few thousand nodes, which is why such a pair also turns up in a
    /// long-running process rather than only in theory.
    /// </remarks>
    private static (ContainerRenderNode Replaced, ContainerRenderNode Replacement) FindRuntimeHashCollidingPair()
    {
        const int Budget = 200_000;
        var byHash = new Dictionary<int, ContainerRenderNode>();
        for (int attempt = 0; attempt < Budget; attempt++)
        {
            var candidate = new ContainerRenderNode();
            if (byHash.TryGetValue(RuntimeHelpers.GetHashCode(candidate), out ContainerRenderNode? existing))
                return (existing, candidate);

            byHash[RuntimeHelpers.GetHashCode(candidate)] = candidate;
        }

        Assert.Fail(
            $"No two of {Budget} render nodes shared a runtime hash code, so this runtime no longer produces "
            + "the collision the regression is about. Rewrite the fixture rather than deleting the test.");
        return default;
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
