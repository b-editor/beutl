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

        var hitTestState = new RectangleHitTestState(
            rect,
            fill is not null,
            pen?.StrokeAlignment ?? StrokeAlignment.Inside,
            pen?.Thickness ?? 0);

        var state = (rect, hitTestState);
        context.Publish(context.PaintedSource(
            state,
            draw: static (canvas, fill, pen, state) =>
                canvas.DrawRectangle(state.rect, fill, pen),
            fill: fill,
            pen: pen,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.Custom(
                hitTestState,
                static (state, context, point) => state.HitTest(context, point)),
            scale: RenderScaleContract.Vector));
    }

    private readonly record struct RectangleHitTestState(
        Rect Rect,
        bool HasFill,
        StrokeAlignment StrokeAlignment,
        float Thickness)
    {
        public bool HitTest(RenderHitTestContext context, Point point) => HitTest(point);

        public bool HitTest(Point point)
        {
            float realThickness = PenHelper.GetRealThickness(StrokeAlignment, Thickness);

            if (HasFill)
            {
                Rect rect = Rect.Inflate(realThickness);
                return rect.ContainsExclusive(point);
            }

            Rect borderRect = Rect.Inflate(realThickness);
            Rect emptyRect = Rect.Deflate(realThickness);
            return borderRect.ContainsExclusive(point) && !emptyRect.ContainsExclusive(point);
        }
    }
}
