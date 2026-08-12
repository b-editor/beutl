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

        RenderResource<Geometry.Resource> geometryResource = context.Borrow(geometry);
        var hitTestState = new GeometryHitTestState(geometry, fill, pen);
        RenderResource<GeometryHitTestState> hitTestResource = context.Borrow(hitTestState);

        context.Publish(context.PaintedSource(
            state: geometry,
            draw: static (canvas, fill, pen, state) =>
                canvas.DrawGeometry(state, fill, pen),
            fill: fill,
            pen: pen,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.FromResource(
                hitTestResource,
                static (state, point) => state.HitTest(point)),
            scale: RenderScaleContract.Vector,
            resources: DeferredOpaqueSource.Resources(
                geometryResource,
                hitTestResource)));
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

}

internal static class DeferredOpaqueSource
{
    public static IReadOnlyList<RenderResource> Resources(params RenderResource?[] resources)
    {
        return resources
            .Where(static resource => resource is not null)
            .Select(static resource => resource!)
            .DistinctBy(static resource => resource.SlotIdentity)
            .ToArray();
    }
}
