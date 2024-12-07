using Beutl.Media;

namespace Beutl.Graphics.Rendering.V2;

public sealed class GeometryRenderNode(Geometry geometry, IBrush? fill, IPen? pen)
    : BrushRenderNode(fill, pen)
{
    private readonly int _version = geometry.Version;

    public Geometry Geometry { get; private set; } = geometry;

    public bool Equals(Geometry geometry, IBrush? fill, IPen? pen)
    {
        return Geometry == geometry
               && _version == geometry.Version
               && EqualityComparer<IBrush?>.Default.Equals(Fill, fill)
               && EqualityComparer<IPen?>.Default.Equals(Pen, pen);
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(
                bounds: PenHelper.CalculateBoundsWithStrokeCap(Geometry.GetRenderBounds(Pen), Pen),
                render: canvas => canvas.DrawGeometry(Geometry, Fill, Pen),
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
        return (Fill != null && Geometry.FillContains(point))
               || (Pen != null && Geometry.StrokeContains(Pen, point));
    }
}
