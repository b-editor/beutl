using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class CapturedResourceBorrowContractTests
{
    [Test]
    public void CapturedSnapshotBorrow_RecordsTheBackingObjectIdAndTheSnapshotVersion()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 20 }, Height = { CurrentValue = 20 } };
        using Geometry.Resource resource = (Geometry.Resource)geometry.ToResource(CompositionContext.Default);
        (Geometry.Resource Resource, int Version) captured = resource.Capture()!.Value;
        resource.Version = captured.Version + 3;

        RenderResourceIdentity fromCapture = default;
        RenderResourceIdentity fromArguments = default;
        using var node = new DelegateSourceNode(context =>
        {
            fromCapture = context.Borrow(captured).CacheIdentity;
            fromArguments = context.Borrow(
                    captured.Resource,
                    captured.Resource.GetOriginal().Id,
                    captured.Version)
                .CacheIdentity;
        });

        _ = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(fromCapture, Is.EqualTo(fromArguments));
            Assert.That(fromCapture.Key, Is.EqualTo(geometry.Id));
            Assert.That(fromCapture.Version, Is.EqualTo(captured.Version));
        });
    }

    [Test]
    public void CapturedSnapshotBorrow_ServesAResourceWithNoBackingObject()
    {
        using var detached = new EngineObject.Resource();
        (EngineObject.Resource Resource, int Version) captured = detached.Capture()!.Value;

        RenderResourceIdentity first = default;
        RenderResourceIdentity second = default;
        using var firstNode = new DelegateSourceNode(c => first = c.Borrow(captured).CacheIdentity);
        using var secondNode = new DelegateSourceNode(c => second = c.Borrow(captured).CacheIdentity);

        _ = Measure(firstNode);
        _ = Measure(secondNode);

        Assert.Multiple(() =>
        {
            Assert.That(first.Key, Is.Not.Null);
            Assert.That(first, Is.EqualTo(second),
                "a resource with no backing EngineObject still coalesces with itself across requests");
        });
    }

    /// <summary>
    /// The two overloads sit one token apart and have opposite output-cache semantics, so the difference is
    /// pinned rather than left to the reader of the call site.
    /// </summary>
    [Test]
    public void ADefaultedCacheKey_NeverCoalescesWhereTheSnapshotOverloadAlwaysDoes()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 20 }, Height = { CurrentValue = 20 } };
        using Geometry.Resource resource = (Geometry.Resource)geometry.ToResource(CompositionContext.Default);
        (Geometry.Resource Resource, int Version) captured = resource.Capture()!.Value;

        List<RenderResourceIdentity> defaulted = [];
        List<RenderResourceIdentity> snapshot = [];
        using var node = new DelegateSourceNode(context =>
        {
            defaulted.Add(context.Borrow(captured.Resource).CacheIdentity);
            snapshot.Add(context.Borrow(captured).CacheIdentity);
        });

        _ = Measure(node);
        _ = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(defaulted[0], Is.Not.EqualTo(defaulted[1]),
                "a null cache key takes a fresh request-local identity, so nothing declaring it is ever reused");
            Assert.That(snapshot[0], Is.EqualTo(snapshot[1]),
                "the snapshot overload derives a coalescing key, so the same resource reaches the same entry");
        });
    }

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });
        return renderer.Measure();
    }

    private sealed class DelegateSourceNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }
}
