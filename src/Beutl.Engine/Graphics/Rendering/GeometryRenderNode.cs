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
            EngineResourceIdentity.Of(geometry),
            geometrySnapshot.Version,
            fill is null ? null : EngineResourceIdentity.Of(fill),
            fillSnapshot?.Version,
            pen is null ? null : EngineResourceIdentity.Of(pen),
            penSnapshot?.Version);
        RenderResource<GeometryHitTestState> hitTestResource = context.Borrow(
            hitTestState,
            hitTestIdentity);

        context.Publish(context.PaintedSource(
            state: (geometry, bounds),
            draw: static (canvas, fill, pen, state) =>
                canvas.DrawGeometry(state.geometry, fill, pen),
            fill: fillSnapshot,
            pen: penSnapshot,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.FromResource(
                hitTestResource,
                static (state, point) => state.HitTest(point),
                typeof(GeometryHitTestState)),
            scale: RenderScaleContract.Vector,
            structuralKey: typeof(GeometryRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity(bounds),
            resources: DeferredOpaqueSource.Resources(
                ("geometry", geometryResource),
                ("hitTest", hitTestResource))));
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

    public static IReadOnlyList<RenderResourceBinding> Resources(
        params (string Name, RenderResource? Resource)[] resources)
    {
        return resources
            .Where(static item => item.Resource is not null)
            .DistinctBy(static item => item.Resource!.SlotIdentity)
            .Select(static item => new RenderResourceBinding(item.Name, item.Resource!))
            .ToArray();
    }

    private sealed class ResourceCacheKey
    {
    }
}
