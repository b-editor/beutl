using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryClipRenderNode(Geometry.Resource clip, ClipOperation operation) : ContainerRenderNode
{
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

        HasChanges = true;
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
        Guid geometryId = clip.Resource.GetOriginal().Id;
        var boundsMetadata = new GeometryClipBoundsMetadata(clip.Resource.Bounds, operation);
        RenderResource<Geometry.Resource> resource = context.Borrow(
            clip.Resource,
            cacheKey: geometryId,
            version: clip.Version);
        var hitTestState = new GeometryClipHitTestState(clip.Resource, operation);
        RenderResource<GeometryClipHitTestState> hitTestResource = context.Borrow(
            hitTestState,
            cacheKey: (geometryId, clip.Version, operation));
        TargetScopeDescription description = TargetScopeDescription.Create(
            session => session.UseResource(resource, geometry =>
                session.Canvas.Use(canvas =>
                {
                    using (canvas.PushClip(geometry, operation))
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
            structuralKey: typeof(GeometryClipRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity((geometryId, clip.Version, operation)),
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
