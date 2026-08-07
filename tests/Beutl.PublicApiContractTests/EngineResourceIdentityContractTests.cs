using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Covers the public derivation an out-of-tree render node needs when an engine resource's identity feeds a
/// hit-test or structural key rather than a <c>Borrow</c> registration.
/// </summary>
/// <remarks>
/// <c>Borrow</c> derives the same identity but necessarily registers a borrow, so it is not an option for a key
/// the node only wants to compare. Without a public derivation the only route left is
/// <c>GetOriginal().Id</c>, which throws for a resource that never went through
/// <see cref="EngineObject.ToResource"/> — and a resource is publicly constructible and subclassable, so that
/// is a shape a plugin reaches without doing anything unusual.
/// </remarks>
[TestFixture]
public sealed class EngineResourceIdentityContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void Of_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => EngineResourceIdentity.Of(null!));
    }

    [Test]
    public void ADetachedResource_ThrowsOnTheOnlyOtherPublicRouteToItsIdentity()
    {
        using var detached = new PluginResource();

        Assert.Throws<NullReferenceException>(() => _ = detached.GetOriginal().Id);
    }

    [Test]
    public void Of_OnADetachedResource_IsStablePerInstanceAndDistinctBetweenInstances()
    {
        using var first = new PluginResource();
        using var second = new PluginResource();

        Assert.Multiple(() =>
        {
            Assert.That(EngineResourceIdentity.Of(first), Is.EqualTo(EngineResourceIdentity.Of(first)));
            Assert.That(EngineResourceIdentity.Of(first), Is.Not.EqualTo(EngineResourceIdentity.Of(second)));
        });
    }

    [Test]
    public void Of_OnADetachedResource_NeverCollidesWithABackingId()
    {
        using var detached = new PluginResource();
        Brush.Resource attached = Brushes.Resource.White;

        Assert.That(
            EngineResourceIdentity.Of(detached),
            Is.Not.EqualTo(EngineResourceIdentity.Of(attached)),
            "a synthesized identity is a distinct type, so no id a caller assigns can be made to match it");
    }

    [Test]
    public void Of_OnAnAttachedResource_IsTheBackingObjectId()
    {
        Brush.Resource attached = Brushes.Resource.White;

        Assert.That(EngineResourceIdentity.Of(attached), Is.EqualTo(attached.GetOriginal().Id));
    }

    [Test]
    public void APluginNode_KeysAHitTestResourceOnADetachedResourceWithoutThrowing()
    {
        using var resource = new PluginResource { Version = 3 };
        using var node = new PluginHitTestNode(resource);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(renderer.HitTest(new Point(4, 4)), Is.True);
            Assert.That(renderer.HitTest(new Point(40, 40)), Is.False);
        });
    }

    private sealed class PluginResource : EngineObject.Resource;

    private sealed class PluginHitTestNode(EngineObject.Resource resource) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            var identity = new PluginResourceIdentity(
                EngineResourceIdentity.Of(resource),
                resource.Version);
            var state = new PluginHitTestState(s_bounds);

            context.Publish(context.PaintedSource(
                state: s_bounds,
                draw: static (session, _) => session.Canvas.Clear(Colors.Red),
                fill: null,
                pen: null,
                brushBounds: s_bounds,
                outputBounds: s_bounds,
                hitTest: RenderHitTestContract.Custom(
                    (_, point) => state.HitTest(point),
                    structuralKey: (typeof(PluginHitTestNode), "hit-test", identity)),
                scale: RenderScaleContract.Vector,
                structuralKey: (typeof(PluginHitTestNode), identity)));
        }
    }

    private sealed class PluginHitTestState(Rect bounds)
    {
        public bool HitTest(Point point) => bounds.Contains(point);
    }

    private readonly record struct PluginResourceIdentity(object ResourceId, int Version);
}
