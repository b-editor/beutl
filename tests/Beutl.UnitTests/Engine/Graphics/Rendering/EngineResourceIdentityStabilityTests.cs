using System.Reflection;
using System.Runtime.CompilerServices;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Proves that routing the six <c>GetOriginal().Id</c> sites through <see cref="EngineResourceIdentity.Of"/>
/// moves no attached resource's recorded identity.
/// </summary>
/// <remarks>
/// <para>
/// The two short nodes are compared against a verbatim reconstruction of their pre-change <c>Process</c>,
/// recorded in the same process, so the whole declared list and the runtime identity are compared element by
/// element rather than read off a diff. The reconstruction's own identity types are re-declared here, so the
/// comparison flattens every composite key into its leaf elements and their declared types.
/// </para>
/// <para>
/// The remaining three nodes have <c>Process</c> bodies whose bulk is private helpers and backend setup, so
/// their recorded identity elements are compared against the pre-change expression evaluated verbatim here
/// instead of against a reconstructed node.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EngineResourceIdentityStabilityTests
{
    [Test]
    public void GeometryRenderNode_RecordsWhatThePreChangeProcessRecorded()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 40 }, Height = { CurrentValue = 30 } };
        var fill = new SolidColorBrush(Colors.Red);
        var pen = new Pen { Brush = { CurrentValue = Brushes.Black }, Thickness = { CurrentValue = 2 } };
        using Beutl.Media.Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
        using SolidColorBrush.Resource fillResource = fill.ToResource(CompositionContext.Default);
        using Pen.Resource penResource = pen.ToResource(CompositionContext.Default);
        using var shipped = new GeometryRenderNode(geometryResource, fillResource, penResource);
        using var preChange = new PreChangeGeometryRenderNode(geometryResource, fillResource, penResource);

        IReadOnlyList<string> shippedIdentity = OpaqueIdentity(shipped);
        IReadOnlyList<string> preChangeIdentity = OpaqueIdentity(preChange);

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, shippedIdentity));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shippedIdentity, Is.Not.Empty);
            Assert.That(shippedIdentity, Is.EqualTo(preChangeIdentity).AsCollection);
            Assert.That(shippedIdentity, Has.Some.EqualTo($"System.Guid={geometry.Id}"));
        }
    }

    [Test]
    public void GeometryClipRenderNode_RecordsWhatThePreChangeProcessRecorded()
    {
        var geometry = new EllipseGeometry { Width = { CurrentValue = 40 }, Height = { CurrentValue = 30 } };
        using Beutl.Media.Geometry.Resource geometryResource = geometry.ToResource(CompositionContext.Default);
        using var shipped = new GeometryClipRenderNode(geometryResource, ClipOperation.Intersect);
        shipped.AddChild(new RectangleRenderNode(new Rect(0, 0, 16, 16), null, null));
        using var preChange = new PreChangeGeometryClipRenderNode(geometryResource, ClipOperation.Intersect);
        preChange.AddChild(new RectangleRenderNode(new Rect(0, 0, 16, 16), null, null));

        IReadOnlyList<string> shippedIdentity = TargetScopeIdentity(shipped);
        IReadOnlyList<string> preChangeIdentity = TargetScopeIdentity(preChange);

        TestContext.Out.WriteLine(string.Join(Environment.NewLine, shippedIdentity));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shippedIdentity, Is.Not.Empty);
            Assert.That(shippedIdentity, Is.EqualTo(preChangeIdentity).AsCollection);
            Assert.That(shippedIdentity, Has.Some.EqualTo($"System.Guid={geometry.Id}"));
        }
    }

    [Test]
    public void ParticleRenderNode_KeepsTheSnapshotIdentityElementsThePreChangeExpressionProduces()
    {
        var particle = new RectShape();
        particle.Width.CurrentValue = 8;
        particle.Height.CurrentValue = 8;
        particle.Fill.CurrentValue = Brushes.White;
        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = particle;
        using var resource =
            (ParticleEmitter.Resource)emitter.ToResource(new CompositionContext(TimeSpan.FromSeconds(1)));
        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
            "precondition: the identity site is only reached when the emitter has alive particles");
        using var node = new ParticleRenderNode(resource);

        IReadOnlyList<string> recorded = DeclaredKeys(node, RenderFragmentKind.TargetCommand);

        Assert.That(recorded, Is.EqualTo(Describe(
            (resource.GetOriginal().Id, resource.Version))).AsCollection);
    }

    [Test]
    public void FilterEffectRenderNode_KeepsTheSegmentOwnKeyElementsThePreChangeExpressionProduces()
    {
        var effect = new ShakeEffect();
        effect.StrengthX.CurrentValue = 4;
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var node = new FilterEffectRenderNode(resource);
        node.AddChild(new RectangleRenderNode(new Rect(0, 0, 16, 16), null, null));

        IReadOnlyList<string> recorded = DeclaredKeys(node, RenderFragmentKind.LegacyFilterEffect);

        Assert.That(recorded, Is.EqualTo(Describe(
            (typeof(FilterEffectRenderNode), resource.GetOriginal().Id, 0))).AsCollection);
    }

    [Test]
    public void Scene3DRenderNode_KeepsTheSnapshotAndRuntimeIdentityElementsThePreChangeExpressionProduces()
    {
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = 32;
        scene.RenderHeight.CurrentValue = 24;
        using var resource = (Scene3D.Resource)scene.ToResource(CompositionContext.Default);
        using var node = new Scene3DRenderNode(resource);
        var bounds = new Rect(0, 0, 32, 24);

        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var payload = (OpaqueRenderFragmentPayload)SingleRoot(graph).Payload!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Describe(payload.Description.Resources[0].CacheIdentity.Key),
                Is.EqualTo(Describe((resource.GetOriginal().Id, resource.Version))).AsCollection);
            Assert.That(
                Describe(payload.Description.RuntimeIdentity!.Value.Key),
                Is.EqualTo(Describe((resource.GetOriginal().Id, resource.Version, bounds))).AsCollection);
        }
    }

    private static IReadOnlyList<string> OpaqueIdentity(RenderNode node)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var payload = (OpaqueRenderFragmentPayload)SingleRoot(graph).Payload!;
        var result = new List<string>();
        AppendResources(result, payload.Description.Resources);
        result.Add("runtime:");
        Append(result, null, payload.Description.RuntimeIdentity?.Key);
        return result;
    }

    private static IReadOnlyList<string> TargetScopeIdentity(RenderNode node)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        var payload = (TargetScopeRenderFragmentPayload)SingleRoot(graph).Payload!;
        var result = new List<string>();
        AppendResources(result, payload.Description.Resources);
        result.Add("runtime:");
        Append(result, null, payload.Description.RuntimeIdentity?.Key);
        return result;
    }

    private static IReadOnlyList<string> DeclaredKeys(RenderNode node, RenderFragmentKind kind)
    {
        using var owner = new RenderRequestOwner();
        using var request = CreateRequest(owner);
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference fragment = graph.Fragments
            .Select(static item => (RenderFragmentReference)item.Payload!)
            .Single(item => item.Kind == kind);
        IReadOnlyList<RenderResource> resources = fragment.Payload switch
        {
            TargetCommandRenderFragmentPayload command => command.Description.Resources,
            LegacyFilterEffectRenderFragmentPayload legacy => [legacy.Context],
            _ => throw new InvalidOperationException($"Unexpected payload {fragment.Payload}."),
        };
        var result = new List<string>();
        Append(result, null, resources[0].CacheIdentity.Key);
        return result;
    }

    private static void AppendResources(List<string> result, IReadOnlyList<RenderResource> resources)
    {
        result.Add($"count={resources.Count}");
        foreach (RenderResource resource in resources)
        {
            Append(result, null, resource.CacheIdentity.Key);
            result.Add($"version={resource.CacheIdentity.Version}");
        }
    }

    private static IReadOnlyList<string> Describe(object? key)
    {
        var result = new List<string>();
        Append(result, null, key);
        return result;
    }

    private static void Append(List<string> result, Type? declared, object? value)
    {
        if (value is null)
        {
            result.Add($"{Name(declared)}=null");
            return;
        }

        Type runtime = value.GetType();
        if (IsComposite(runtime))
        {
            foreach (FieldInfo field in runtime
                         .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .OrderBy(static field => field.MetadataToken))
            {
                Append(result, field.FieldType, field.GetValue(value));
            }

            return;
        }

        result.Add($"{Name(declared ?? runtime)}={Format(value)}");
    }

    private static bool IsComposite(Type type)
        => type is { IsValueType: true, IsPrimitive: false, IsEnum: false }
           && (typeof(ITuple).IsAssignableFrom(type)
               || type.GetMethod(
                   "PrintMembers",
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null);

    private static string Name(Type? type)
        => type is null ? "<unknown>"
            : Nullable.GetUnderlyingType(type) is { } underlying ? $"{underlying.FullName}?"
            : type.FullName!;

    private static string Format(object value)
        => value is Type type ? type.FullName! : value.ToString()!;

    private static RenderRequest CreateRequest(RenderRequestOwner owner)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            targetDomain: new Rect(0, 0, 64, 64),
            cachePolicy: RenderCacheOptions.Disabled,
            owner: owner));

    private static RenderFragmentReference SingleRoot(RecordedRenderGraph graph)
    {
        RenderFragmentId rootId = graph.PublicationRoots.Single();
        return (RenderFragmentReference)graph.Fragments
            .Single(fragment => fragment.Id == rootId)
            .Payload!;
    }

    // Verbatim copy of GeometryRenderNode.Process at 74deae450, before the identity reads were routed.
    private sealed class PreChangeGeometryRenderNode(
        Beutl.Media.Geometry.Resource geometry,
        Brush.Resource? fill,
        Pen.Resource? pen)
        : BrushRenderNode(fill, pen)
    {
        public (Beutl.Media.Geometry.Resource Resource, int Version)? Geometry { get; private set; } = geometry.Capture();

        public override void Process(RenderNodeContext context)
        {
            if (Geometry is not { } geometrySnapshot)
                return;

            (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
            (Pen.Resource Resource, int Version)? penSnapshot = Pen;
            Beutl.Media.Geometry.Resource geometry = geometrySnapshot.Resource;
            Brush.Resource? fill = fillSnapshot?.Resource;
            Pen.Resource? pen = penSnapshot?.Resource;
            Rect bounds = PenHelper.CalculateBoundsWithStrokeCap(
                geometry.GetRenderBounds(pen),
                pen);
            if (bounds.Width == 0 || bounds.Height == 0)
                return;

            RenderResource<Beutl.Media.Geometry.Resource> geometryResource = context.Borrow(geometrySnapshot);
            var hitTestState = new GeometryHitTestState(geometry, fill, pen);
            var hitTestIdentity = new GeometryHitTestIdentity(
                geometry.GetOriginal().Id,
                geometrySnapshot.Version,
                fill?.GetOriginal().Id,
                fillSnapshot?.Version,
                pen?.GetOriginal().Id,
                penSnapshot?.Version);
            RenderResource<GeometryHitTestState> hitTestResource = context.Borrow(
                hitTestState,
                hitTestIdentity);

            context.Publish(context.PaintedSource(
                primary: geometryResource,
                state: bounds,
                draw: static (session, geometry, _) =>
                    session.Canvas.DrawGeometry(geometry, session.Fill, session.Pen),
                fill: fillSnapshot,
                pen: penSnapshot,
                brushBounds: bounds,
                outputBounds: bounds,
                hitTest: RenderHitTestContract.FromResource(
                    hitTestResource,
                    static (state, point) => state.HitTest(point),
                    typeof(GeometryHitTestState)),
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(GeometryRenderNode),
                resources: [hitTestResource]));
        }

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            Geometry = null!;
        }

        private sealed class GeometryHitTestState(
            Beutl.Media.Geometry.Resource geometry,
            Brush.Resource? fill,
            Pen.Resource? pen)
        {
            public bool HitTest(Point point)
            {
                return (fill is not null && geometry.FillContains(point))
                       || (pen is not null && geometry.StrokeContains(pen, point));
            }
        }

        private readonly record struct GeometryHitTestIdentity(
            Guid GeometryId,
            int GeometryVersion,
            Guid? FillId,
            int? FillVersion,
            Guid? PenId,
            int? PenVersion);
    }

    // Verbatim copy of GeometryClipRenderNode.Process at 74deae450, before the identity read was routed.
    private sealed class PreChangeGeometryClipRenderNode(Beutl.Media.Geometry.Resource clip, ClipOperation operation)
        : ContainerRenderNode
    {
        public (Beutl.Media.Geometry.Resource Resource, int Version)? Clip { get; private set; } = clip.Capture();

        public ClipOperation Operation { get; private set; } = operation;

        public override void Process(RenderNodeContext context)
        {
            if (Clip is not { } clip)
            {
                context.PassThrough();
                return;
            }
            if (context.Inputs.Count == 0)
                return;

            ClipOperation operation = Operation;
            Guid geometryId = clip.Resource.GetOriginal().Id;
            var boundsMetadata = new GeometryClipBoundsMetadata(clip.Resource.Bounds, operation);
            RenderResource<Beutl.Media.Geometry.Resource> resource = context.Borrow(clip);
            var hitTestState = new GeometryClipHitTestState(clip.Resource, operation);
            RenderResource<GeometryClipHitTestState> hitTestResource = context.Borrow(
                hitTestState,
                cacheKey: (geometryId, clip.Version, operation));
            TargetScopeDescription description = TargetScopeDescription.Create(
                (geometryId, clip.Version, operation),
                static (session, state) => session.UseDeclaredResource<Beutl.Media.Geometry.Resource>(0, geometry =>
                    session.Canvas.Use(canvas =>
                    {
                        using (canvas.PushClip(geometry, state.operation))
                        {
                            session.ReplayInput();
                        }
                    })),
                RenderBoundsContract.Create(
                    boundsMetadata.TransformBounds,
                    boundsMetadata.GetRequiredInputBounds,
                    structuralKey: (typeof(GeometryClipRenderNode), "clip-bounds")),
                RenderHitTestContract.FromResource(
                    hitTestResource,
                    static (state, hitTest, point) => state.HitTest(hitTest, point),
                    structuralKey: typeof(GeometryClipRenderNode)),
                RenderScaleContract.PreserveInputSupply,
                RenderDeviceGridMapping.Preserved,
                resources: [resource, hitTestResource]);

            foreach (RenderFragmentHandle input in context.Inputs)
            {
                context.Publish(context.TargetScope(input, description));
            }
        }

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            Clip = null!;
        }

        private readonly record struct GeometryClipBoundsMetadata(Rect Bounds, ClipOperation Operation)
        {
            public Rect TransformBounds(Rect value)
                => Operation == ClipOperation.Intersect ? value.Intersect(Bounds) : value;

            public Rect GetRequiredInputBounds(Rect value)
                => Operation == ClipOperation.Intersect ? value.Intersect(Bounds) : value;
        }

        private sealed class GeometryClipHitTestState(
            Beutl.Media.Geometry.Resource geometry,
            ClipOperation operation)
        {
            public bool HitTest(RenderHitTestContext context, Point point)
            {
                bool insideClip = geometry.FillContains(point);
                bool clipAcceptsPoint = operation == ClipOperation.Intersect ? insideClip : !insideClip;
                return clipAcceptsPoint && context.Inputs.Any(input => input.HitTest(point));
            }
        }
    }
}
