using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RectangleNode : BrushDrawNode
{
    public RectangleNode(Rect rect, IBrush? fill, IPen? pen)
        : base(fill, pen, PenHelper.GetBounds(rect, pen))
    {
        Rect = rect;
    }

    public Rect Rect { get; }

    public bool Equals(Rect rect, IBrush? fill, IPen? pen)
    {
        return Rect == rect
            && EqualityComparer<IBrush?>.Default.Equals(Fill, fill)
            && EqualityComparer<IPen?>.Default.Equals(Pen, pen);
    }

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.DrawRectangle(Rect, Fill, Pen);
    }

    public override void Dispose()
    {
    }

    public override bool HitTest(Point point)
    {
        StrokeAlignment alignment = Pen?.StrokeAlignment ?? StrokeAlignment.Inside;
        float thickness = Pen?.Thickness ?? 0;
        thickness = PenHelper.GetRealThickness(alignment, thickness);

        if (Fill != null)
        {
            Rect rect = Rect.Inflate(thickness);
            return rect.ContainsExclusive(point);
        }
        else
        {
            Rect borderRect = Rect.Inflate(thickness);
            Rect emptyRect = Rect.Deflate(thickness);
            return borderRect.ContainsExclusive(point) && !emptyRect.ContainsExclusive(point);
        }
    }
}
