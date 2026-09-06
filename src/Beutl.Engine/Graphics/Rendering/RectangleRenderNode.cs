using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RectangleRenderNode(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public Rect Rect { get; private set; } = rect;

    public bool Update(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = false;
        if (Rect != rect)
        {
            Rect = rect;
            changed = true;
        }

        if (Update(fill, pen))
        {
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
        Rect rect = Rect;
        Brush.Resource? fill = Fill?.Resource;
        Pen.Resource? pen = Pen?.Resource;
        Rect bounds = PenHelper.GetBounds(rect, pen);
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        context.Publish(context.PaintedSource(
            rect,
            draw: static (canvas, fill, pen, rect) =>
                canvas.DrawRectangle(rect, fill, pen),
            fill: fill,
            pen: pen,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.Custom(HitTest),
            scale: RenderScaleContract.Vector));
    }

    private bool HitTest(RenderHitTestContext _, Point point)
    {
        Rect rect = Rect;
        Pen.Resource? pen = Pen?.Resource;
        float realThickness = PenHelper.GetRealThickness(
            pen?.StrokeAlignment ?? StrokeAlignment.Inside,
            pen?.Thickness ?? 0);

        if (Fill?.Resource is not null)
        {
            return rect.Inflate(realThickness).ContainsExclusive(point);
        }

        Rect borderRect = rect.Inflate(realThickness);
        Rect emptyRect = rect.Deflate(realThickness);
        return borderRect.ContainsExclusive(point) && !emptyRect.ContainsExclusive(point);
    }
}
