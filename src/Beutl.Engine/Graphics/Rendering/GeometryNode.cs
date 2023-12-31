using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class GeometryNode(Geometry geometry, IBrush? fill, IPen? pen)
    : BrushDrawNode(fill, pen, PenHelper.CalculateBoundsWithStrokeCap(geometry.GetRenderBounds(pen), pen))
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

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.DrawGeometry(Geometry, Fill, Pen);
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        Geometry = null!;
    }

    public override bool HitTest(Point point)
    {
        return (Fill != null && Geometry.FillContains(point))
            || (Pen != null && Geometry.StrokeContains(Pen, point));
    }
}
