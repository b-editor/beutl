using Beutl.Media;

namespace Beutl.Graphics.Rendering.V2;

public sealed class RectangleRenderNode(Rect rect, IBrush? fill, IPen? pen)
    : BrushRenderNode(fill, pen)
{
    public Rect Rect { get; } = rect;

    public bool Equals(Rect rect, IBrush? fill, IPen? pen)
    {
        return Rect == rect
               && EqualityComparer<IBrush?>.Default.Equals(Fill, fill)
               && EqualityComparer<IPen?>.Default.Equals(Pen, pen);
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(PenHelper.GetBounds(Rect, Pen),
                canvas => canvas.DrawRectangle(Rect, Fill, Pen), HitTest)
        ];
    }

    private bool HitTest(Point point)
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
