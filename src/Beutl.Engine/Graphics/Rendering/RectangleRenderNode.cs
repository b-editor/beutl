using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RectangleRenderNode(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public Rect Rect { get; set; } = rect;

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

        HasChanges = changed;
        return changed;
    }

    public override void Process(RenderNodeContext context)
    {
        Rect rect = Rect;
        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        Rect bounds = PenHelper.GetBounds(rect, pen);
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        var hitTestState = new RectangleHitTestState(
            rect,
            fill is not null,
            pen?.StrokeAlignment ?? StrokeAlignment.Inside,
            pen?.Thickness ?? 0);

        context.Publish(context.PaintedSource(
            state: (rect, hitTestState),
            draw: static (session, state) =>
                session.Canvas.DrawRectangle(state.rect, session.Fill, session.Pen),
            fill: fillSnapshot,
            pen: penSnapshot,
            brushBounds: bounds,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.Custom((_, point) => hitTestState.HitTest(point)),
            scale: RenderScaleContract.Vector,
            structuralKey: typeof(RectangleRenderNode)));
    }

    private readonly record struct RectangleHitTestState(
        Rect Rect,
        bool HasFill,
        StrokeAlignment StrokeAlignment,
        float Thickness)
    {
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
