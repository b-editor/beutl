using System.Runtime.CompilerServices;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryRenderNode(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public (Geometry.Resource Resource, int Version)? Geometry { get; private set; } = geometry.Capture();

    public bool Update(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        if (!geometry.Compare(Geometry))
        {
            Geometry = geometry.Capture();
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
        if (Geometry is not { } geometrySnapshot)
            return;

        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        Geometry.Resource geometry = geometrySnapshot.Resource;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        Rect bounds = PenHelper.CalculateBoundsWithStrokeCap(
            geometry.GetRenderBounds(pen),
            pen);
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        RenderResource<Geometry.Resource> geometryResource = context.Borrow(geometrySnapshot);
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
        Geometry.Resource geometry,
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

internal static class DeferredOpaqueSource
{
    private static readonly ConditionalWeakTable<object, ResourceCacheKey> s_resourceKeys = new();

    public static object GetCacheKey(object resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource is EngineObject.Resource engineResource
            ? EngineResourceIdentity.Of(engineResource)
            : s_resourceKeys.GetValue(resource, static _ => new ResourceCacheKey());
    }

    public static IReadOnlyList<RenderResource> Resources(params RenderResource?[] resources)
    {
        return resources
            .OfType<RenderResource>()
            .DistinctBy(static resource => resource.SlotIdentity)
            .ToArray();
    }

    private sealed class ResourceCacheKey
    {
    }
}
