using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Covers the six render-node sites that read <c>GetOriginal().Id</c> directly and now route through
/// <see cref="EngineResourceIdentity.Of"/>.
/// </summary>
/// <remarks>
/// Each site keeps a <see cref="Guid"/>-typed identity element, so the routing has to stay allocation-free to be
/// worth taking. The recorded end-to-end outcomes below are what each node actually does with a detached
/// resource, measured rather than reasoned about. Which site a detached resource reaches first depends on which
/// of a node's resources is detached, so the outcome is recorded per input shape rather than per node.
/// <c>Geometry.Resource</c> now builds its path from itself, so a detached geometry no longer fails ahead of the
/// routed identity read; <see cref="DetachedGeometryResourceTests"/> covers that path.
/// </remarks>
[TestFixture]
public sealed class EngineResourceIdentityRoutingTests
{
    private const int Iterations = 20000;
    private const int Rounds = 5;

    private static GeometryHitTestIdentityShape s_geometrySink;
    private static Guid s_geometryClipSink;
    private static (Guid Id, int Version, ClipOperation Operation) s_geometryClipStateSink;
    private static ParticleSnapshotIdentityShape s_particleSink;
    private static (Type Owner, Guid Id, int Segment) s_filterEffectSink;
    private static SceneSnapshotIdentityShape s_sceneSnapshotSink;
    private static SceneRuntimeIdentityShape s_sceneRuntimeSink;

    [Test]
    public void ADetachedResourceOfEveryRoutedSiteType_DerivesAnIdentityInsteadOfThrowing()
    {
        using var geometry = new EllipseGeometry.Resource();
        using var brush = new SolidColorBrush.Resource();
        using var pen = new Pen.Resource();
        using var emitter = new ParticleEmitter.Resource();
        using var effect = new ShakeEffect.Resource();
        using var scene = new Scene3D.Resource();
        EngineObject.Resource[] resources = [geometry, brush, pen, emitter, effect, scene];

        using (Assert.EnterMultipleScope())
        {
            foreach (EngineObject.Resource resource in resources)
            {
                Assert.That(resource.GetOriginal(), Is.Null,
                    $"{resource.GetType()} is detached, so it has no backing id");
                Guid first = Guid.Empty;
                Assert.DoesNotThrow(() => first = EngineResourceIdentity.Of(resource));
                Assert.That(EngineResourceIdentity.Of(resource), Is.EqualTo(first),
                    "the synthesized identity is held weakly against the resource and survives between reads");
                Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            }
        }
    }

    /// <summary>
    /// The one site this change rescues end to end. A detached <see cref="Brush.Resource"/> reaches
    /// <see cref="GeometryRenderNode"/>'s identity read before anything else dereferences it, so the routing
    /// turns a <see cref="NullReferenceException"/> into a complete render. Both the node's constructor and
    /// <c>GraphicsContext2D.DrawGeometry</c> take a publicly constructible <c>Brush.Resource?</c>, so this is
    /// an ordinary plugin shape rather than a contrived one.
    /// </summary>
    [Test]
    public void GeometryRenderNode_WithADetachedFill_RendersInsteadOfThrowing()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 40 }, Height = { CurrentValue = 30 } };
        using Beutl.Media.Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
        using var fill = new SolidColorBrush.Resource();
        using var node = new GeometryRenderNode(geometryResource, fill, null);

        Exception? failure = RecordAndCaptureFailure(node);

        Assert.That(failure, Is.Null,
            "with the geometry attached, the detached fill's identity read is the first dereference, "
            + "so the routing has to rescue it");
    }

    [Test]
    public void GeometryRenderNode_WithADetachedGeometry_ReachesItsRoutedIdentityRead()
    {
        using var geometry = new EllipseGeometry.Resource { Width = 40, Height = 30 };
        using var node = new GeometryRenderNode(geometry, null, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(RecordAndCaptureFailure(node), Is.Null,
                "GetRenderBounds no longer dereferences the backing object, so recording reaches the routed read");
            Assert.That(RecordedFragmentCount(node), Is.EqualTo(1));
        }
    }

    [Test]
    public void GeometryClipRenderNode_WithADetachedGeometry_RecordsItsScope()
    {
        using var geometry = new EllipseGeometry.Resource { Width = 40, Height = 30 };
        using var node = new GeometryClipRenderNode(geometry, ClipOperation.Intersect);
        node.AddChild(new RectangleRenderNode(new Rect(0, 0, 8, 8), null, null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(RecordAndCaptureFailure(node), Is.Null,
                "the routed identity read and the Bounds read one statement later both survive detachment now");
            Assert.That(RecordedFragmentCount(node), Is.EqualTo(1));
        }
    }

    [Test]
    public void ParticleRenderNode_NeverReachesItsIdentityRead_BecauseADetachedEmitterHasNoAliveParticles()
    {
        using var emitter = new ParticleEmitter.Resource();
        using var node = new ParticleRenderNode(emitter);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(emitter.GetAliveParticles().Length, Is.Zero,
                "the simulator field is initialized inline but only Update ever simulates");
            Assert.That(RecordAndCaptureFailure(node), Is.Null);
            Assert.That(RecordedFragmentCount(node), Is.Zero);
        }
    }

    [Test]
    public void Scene3DRenderNode_NeverReachesItsIdentityReads_BecauseADetachedSceneHasNoCamera()
    {
        using var scene = new Scene3D.Resource();
        using var node = new Scene3DRenderNode(scene);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scene.Camera, Is.Null);
            Assert.That(RecordAndCaptureFailure(node), Is.Null);
            Assert.That(RecordedFragmentCount(node), Is.Zero);
        }
    }

    [Test]
    public void ADetachedResourceBorrowedTwiceInOneRequest_CoalescesOntoOneSlot()
    {
        using var detached = new EngineObject.Resource();
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        using var node = new TwiceBorrowingNode(detached);

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var payload = (OpaqueRenderFragmentPayload)SingleRoot(graph).Payload!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(payload.Description.Resources, Has.Count.EqualTo(2));
            Assert.That(payload.Description.Resources[0].Resource.SlotIdentity,
                Is.SameAs(payload.Description.Resources[1].Resource.SlotIdentity),
                "registry coalescing compares keys with Equals, and two boxed equal Guids satisfy that");
            Assert.That(payload.Description.Resources[0].Resource.CacheIdentity.Key,
                Is.EqualTo(EngineResourceIdentity.Of(detached)));
        }
    }

    [Test]
    public void EveryRoutedSite_BuildsItsIdentityWithoutAllocating()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 40 }, Height = { CurrentValue = 30 } };
        var fill = new SolidColorBrush(Colors.Red);
        var pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 2 } };
        using Beutl.Media.Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
        using SolidColorBrush.Resource fillResource = fill.ToResource(CompositionContext.Default);
        using Pen.Resource penResource = pen.ToResource(CompositionContext.Default);
        var emitter = new ParticleEmitter();
        using var emitterResource =
            (ParticleEmitter.Resource)emitter.ToResource(new CompositionContext(TimeSpan.FromSeconds(1)));
        var effect = new ShakeEffect();
        using FilterEffect.Resource effectResource = effect.ToResource(CompositionContext.Default);
        var scene = new Scene3D();
        using var sceneResource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        var bounds = new Rect(0, 0, 32, 24);

        (string Site, long Before, long After)[] measurements =
        [
            Compare(
                "GeometryRenderNode.cs:48,50,52",
                () => s_geometrySink = new GeometryHitTestIdentityShape(
                    geometryResource.GetOriginal().Id,
                    geometryResource.Version,
                    fillResource.GetOriginal().Id,
                    fillResource.Version,
                    penResource.GetOriginal().Id,
                    penResource.Version),
                () => s_geometrySink = new GeometryHitTestIdentityShape(
                    EngineResourceIdentity.Of(geometryResource),
                    geometryResource.Version,
                    EngineResourceIdentity.Of(fillResource),
                    fillResource.Version,
                    EngineResourceIdentity.Of(penResource),
                    penResource.Version)),
            Compare(
                "GeometryClipRenderNode.cs:46",
                () =>
                {
                    s_geometryClipSink = geometryResource.GetOriginal().Id;
                    s_geometryClipStateSink =
                        (s_geometryClipSink, geometryResource.Version, ClipOperation.Intersect);
                },
                () =>
                {
                    s_geometryClipSink = EngineResourceIdentity.Of(geometryResource);
                    s_geometryClipStateSink =
                        (s_geometryClipSink, geometryResource.Version, ClipOperation.Intersect);
                }),
            Compare(
                "ParticleRenderNode.cs:55",
                () => s_particleSink = new ParticleSnapshotIdentityShape(
                    emitterResource.GetOriginal().Id,
                    emitterResource.Version),
                () => s_particleSink = new ParticleSnapshotIdentityShape(
                    EngineResourceIdentity.Of(emitterResource),
                    emitterResource.Version)),
            Compare(
                "FilterEffectRenderNode.cs:201",
                () => s_filterEffectSink =
                    (typeof(FilterEffectRenderNode), effectResource.GetOriginal().Id, 0),
                () => s_filterEffectSink =
                    (typeof(FilterEffectRenderNode), EngineResourceIdentity.Of(effectResource), 0)),
            Compare(
                "Scene3DRenderNode.cs:97",
                () => s_sceneSnapshotSink = new SceneSnapshotIdentityShape(
                    sceneResource.GetOriginal().Id,
                    sceneResource.Version),
                () => s_sceneSnapshotSink = new SceneSnapshotIdentityShape(
                    EngineResourceIdentity.Of(sceneResource),
                    sceneResource.Version)),
            Compare(
                "Scene3DRenderNode.cs:117",
                () => s_sceneRuntimeSink = new SceneRuntimeIdentityShape(
                    sceneResource.GetOriginal().Id,
                    sceneResource.Version,
                    bounds),
                () => s_sceneRuntimeSink = new SceneRuntimeIdentityShape(
                    EngineResourceIdentity.Of(sceneResource),
                    sceneResource.Version,
                    bounds)),
        ];

        using (Assert.EnterMultipleScope())
        {
            foreach ((string site, long before, long after) in measurements)
            {
                TestContext.Out.WriteLine(
                    $"{site}: {before} -> {after} bytes per {Iterations} builds");
                Assert.That(after, Is.LessThanOrEqualTo(before), $"{site} got more expensive");
                Assert.That(after, Is.Zero, $"{site} must build its identity without allocating");
            }

            // The sinks give each measured identity somewhere to be stored. Two of them are written but never
            // otherwise read, which is CS0414 and so a build break under this repository's 0-warning bar; these
            // reads exist to answer that compiler warning and assert nothing a reader should rely on.
            _ = s_geometryClipStateSink;
            _ = s_filterEffectSink;
        }
    }

    private static (string Site, long Before, long After) Compare(string site, Action before, Action after)
        => (site, Measure(before), Measure(after));

    private static long Measure(Action build)
    {
        for (int index = 0; index < 200; index++)
            build();

        long best = long.MaxValue;
        for (int round = 0; round < Rounds; round++)
        {
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < Iterations; index++)
                build();
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - start);
        }

        return best;
    }

    private static Exception? RecordAndCaptureFailure(RenderNode node)
    {
        try
        {
            _ = RecordedFragmentCount(node);
            return null;
        }
        catch (Exception ex)
        {
            return Unwrap(ex);
        }
    }

    private static int RecordedFragmentCount(RenderNode node)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        return new RenderRequestRecorder(request).Record(node).PublicationRoots.Count();
    }

    private static Exception Unwrap(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException is { } inner)
            current = inner;
        return current;
    }

    private static Type? DeepestFrameType(Exception exception)
        => new System.Diagnostics.StackTrace(exception).GetFrame(0)?.GetMethod()?.DeclaringType;

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            targetDomain: new Rect(0, 0, 32, 32),
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner));

    private static RenderFragmentReference SingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }

    private readonly record struct GeometryHitTestIdentityShape(
        Guid GeometryId,
        int GeometryVersion,
        Guid? FillId,
        int? FillVersion,
        Guid? PenId,
        int? PenVersion);

    private readonly record struct ParticleSnapshotIdentityShape(Guid ResourceId, int Version);

    private readonly record struct SceneSnapshotIdentityShape(Guid SceneId, int Version);

    private readonly record struct SceneRuntimeIdentityShape(Guid SceneId, int Version, Rect Bounds);

    private sealed class TwiceBorrowingNode(EngineObject.Resource resource) : RenderNode
    {
        private static readonly Rect s_bounds = new(0, 0, 8, 8);

        public override void Process(RenderNodeContext context)
        {
            RenderResource<EngineObject.Resource> first = context.Borrow(
                resource,
                EngineResourceIdentity.Of(resource),
                resource.Version);
            RenderResource<EngineObject.Resource> second = context.Borrow(
                resource,
                EngineResourceIdentity.Of(resource),
                resource.Version);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                s_bounds,
                static (session, bounds) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                resources: [first.Bind("first"), second.Bind("second")]);
            context.Publish(context.OpaqueSource(description));
        }
    }
}
