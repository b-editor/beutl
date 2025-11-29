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

        HasChanges = changed;
        return changed;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(
                bounds: PenHelper.CalculateBoundsWithStrokeCap(Geometry!.Value.Resource.GetRenderBounds(Pen?.Resource), Pen?.Resource),
                render: canvas => canvas.DrawGeometry(Geometry!.Value.Resource, Fill?.Resource, Pen?.Resource),
                hitTest: HitTest
            )
        ];
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Geometry = null!;
    }

    private bool HitTest(Point point)
    {
        return (Fill != null && Geometry!.Value.Resource.FillContains(point))
               || (Pen != null && Geometry!.Value.Resource.StrokeContains(Pen?.Resource, point));
    }
}
