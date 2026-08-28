using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryRenderNode(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    private static readonly RenderResourceSlot<Geometry.Resource> s_geometrySlot = new();
    private static readonly RenderResourceSlot<GeometryHitTestState> s_hitTestSlot = new();

    private static readonly PaintedSourceDefinition<Geometry.Resource> s_definition =
        PaintedSourceDefinition<Geometry.Resource>.Create(
            static (canvas, fill, pen, state) => canvas.DrawGeometry(state, fill, pen),
            RenderHitTestContract.FromSlot(
                s_hitTestSlot,
                static (state, point) => state.HitTest(point)),
            RenderScaleContract.Vector,
            resources: [s_geometrySlot, s_hitTestSlot]);

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
            MarkChanged();
        }

        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        if (Geometry is not { } geometrySnapshot)
            return;

        Geometry.Resource geometry = geometrySnapshot.Resource;
        Brush.Resource? fill = Fill?.Resource;
        Pen.Resource? pen = Pen?.Resource;
        Rect strokeBounds = PenHelper.CalculateBoundsWithStrokeCap(
            geometry.GetRenderBounds(pen),
            pen);
        // DrawGeometry always paints the whole fill path, so a pen whose stroke sits inside the fill
        // (negative Offset, a trimmed or dashed outline) must not shrink the declared output.
        Rect bounds = fill is null ? strokeBounds : strokeBounds.Union(geometry.Bounds);
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        RenderResource<Geometry.Resource> geometryResource = context.Borrow(geometry);
        var hitTestState = new GeometryHitTestState(geometry, fill, pen);
        RenderResource<GeometryHitTestState> hitTestResource = context.Borrow(hitTestState);

        context.Publish(context.PaintedSource(s_definition.Call(
            geometry,
            fill,
            pen,
            OpaqueRenderBoundsContract.Source(bounds),
            [
                s_geometrySlot.Bind(geometryResource),
                s_hitTestSlot.Bind(hitTestResource),
            ])));
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
