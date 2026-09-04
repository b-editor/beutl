using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryRenderNode(Geometry.Resource geometry, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    private static readonly RenderResourceSlot<Geometry.Resource> s_geometrySlot = new();
    private static readonly RenderResourceSlot[] s_slots = [s_geometrySlot];

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
        var hitTestState = new GeometryHitTestState(fill is not null, pen);

        context.Publish(context.PaintedSource(
            geometry,
            static (canvas, fill, pen, state) => canvas.DrawGeometry(state, fill, pen),
            fill,
            pen,
            bounds,
            RenderHitTestContract.FromResource(
                geometryResource,
                hitTestState,
                static (value, state, _, point) => state.HitTest(value, point)),
            RenderScaleContract.Vector,
            bindings: [s_geometrySlot.Bind(geometryResource)],
            slots: s_slots));
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Geometry = null!;
    }

    private readonly record struct GeometryHitTestState(bool HasFill, Pen.Resource? Pen)
    {
        public bool HitTest(Geometry.Resource geometry, Point point)
        {
            return (HasFill && geometry.FillContains(point))
                   || (Pen is not null && geometry.StrokeContains(Pen, point));
        }
    }
}
