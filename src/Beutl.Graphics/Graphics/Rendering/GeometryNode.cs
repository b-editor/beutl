using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryNode : BrushDrawNode
{
    public GeometryNode(Geometry geometry, IBrush? fill, IPen? pen)
        : base(fill, pen, PenHelper.CalculateBoundsWithStrokeCap(geometry.GetRenderBounds(pen), pen))
    {
        Geometry = geometry;
    }

    public Geometry Geometry { get; private set; }

    public bool Equals(Geometry geometry, IBrush? fill, IPen? pen)
    {
        return Geometry == geometry
            && Fill == fill
            && Pen == pen;
    }

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.DrawGeometry(Geometry, Fill, Pen);
    }

    public override void Dispose()
    {
        Geometry = null!;
    }

    public override bool HitTest(Point point)
    {
        return (Fill != null && Geometry.FillContains(point))
            || (Pen != null && Geometry.StrokeContains(Pen, point));
    }
}
