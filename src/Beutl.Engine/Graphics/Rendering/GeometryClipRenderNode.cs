using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryClipRenderNode(Geometry.Resource clip, ClipOperation operation) : ContainerRenderNode
{
    private static readonly RenderResourceSlot<Geometry.Resource> s_geometrySlot = new();
    private static readonly RenderResourceSlot<GeometryClipHitTestState> s_hitTestSlot = new();

    public (Geometry.Resource Resource, int Version)? Clip { get; private set; } = clip.Capture();

    public ClipOperation Operation { get; private set; } = operation;

    public bool Update(Geometry.Resource clip, ClipOperation operation)
    {
        bool changed = false;
        if (!clip.Compare(Clip))
        {
            Clip = clip.Capture();
            changed = true;
        }

        if (Operation != operation)
        {
            Operation = operation;
            changed = true;
        }

        if (changed)
        {
            HasChanges = true;
        }

        return changed;
    }

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
        var boundsMetadata = new GeometryClipBoundsMetadata(clip.Resource.Bounds, operation);
        RenderResource<Geometry.Resource> resource = context.Borrow(clip.Resource);
        var hitTestState = new GeometryClipHitTestState(clip.Resource, operation);
        RenderResource<GeometryClipHitTestState> hitTestResource = context.Borrow(hitTestState);
        TargetScopeDefinition<ClipOperation> definition = TargetScopeDefinition<ClipOperation>.Create(
            static (session, state) => session.UseResource(s_geometrySlot, geometry =>
                session.Canvas.Use(canvas =>
                {
                    using (canvas.PushClip(geometry, state))
                    {
                        session.ReplayInput();
                    }
                })),
            RenderBoundsContract.Create(
                boundsMetadata.TransformBounds,
                boundsMetadata.GetRequiredInputBounds),
            RenderHitTestContract.FromResource(
                hitTestResource,
                static (state, hitTest, point) => state.HitTest(hitTest, point)),
            RenderScaleContract.PreserveInputSupply,
            deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent,
            deviceGridMapping: RenderDeviceGridMapping.Preserved,
            resources: [s_geometrySlot, s_hitTestSlot]);

        context.PublishMappedInputs(
            definition.Call(
                operation,
                [s_geometrySlot.Bind(resource), s_hitTestSlot.Bind(hitTestResource)]),
            static (context, input, value) => context.TargetScope(input, value));
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
        Geometry.Resource geometry,
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
