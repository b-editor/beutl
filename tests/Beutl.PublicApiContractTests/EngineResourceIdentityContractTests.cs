using System.Reflection;
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
/// <c>GetOriginal()?.Id</c>, which has no backing id for a resource that never went through
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
    public void ADetachedResource_HasNoBackingEngineObject()
    {
        using var detached = new PluginResource();

        Assert.That(detached.GetOriginal(), Is.Null);
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

    /// <summary>
    /// Both derivations are <see cref="Guid"/>s boxed into the same cache-key dictionary, so what has to hold
    /// is that each one finds its own entry back and neither displaces the other. Nothing about the types keeps
    /// them apart any more: a synthesized identity and a backing object id are the same shape, and the project
    /// deliberately treats a collision between them as a non-scenario rather than guarding against it.
    /// </summary>
    [Test]
    public void Of_KeysADetachedAndAnAttachedResourceApartInOneCacheKeyDictionary()
    {
        using var detached = new PluginResource();
        Brush.Resource attached = Brushes.Resource.White;

        var keys = new Dictionary<object, string>
        {
            [EngineResourceIdentity.Of(detached)] = "detached",
            [EngineResourceIdentity.Of(attached)] = "attached",
        };

        Assert.Multiple(() =>
        {
            Assert.That(keys, Has.Count.EqualTo(2),
                "a synthesized identity must not land on the same entry as a backing object id");
            Assert.That(keys[EngineResourceIdentity.Of(detached)], Is.EqualTo("detached"),
                "a later read of the same detached resource must hash and compare back to its own entry");
            Assert.That(keys[EngineResourceIdentity.Of(attached)], Is.EqualTo("attached"));
        });
    }

    /// <summary>
    /// The return type is load-bearing: a <see cref="Guid"/> lets a caller hold the identity in a
    /// <see cref="Guid"/>-typed cache-key field without boxing on every <c>Process</c>, which is what lets the
    /// engine's own hit-test and structural keys route through this derivation at all.
    /// </summary>
    [Test]
    public void Of_ReturnsAGuidSoACallerCanHoldItWithoutBoxing()
    {
        MethodInfo method = typeof(EngineResourceIdentity)
            .GetMethod(nameof(EngineResourceIdentity.Of), BindingFlags.Public | BindingFlags.Static)!;

        Assert.That(method.ReturnType, Is.EqualTo(typeof(Guid)));
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
                draw: static (session, state) =>
                    session.Canvas.DrawRectangle(state, session.Fill, session.Pen),
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
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
