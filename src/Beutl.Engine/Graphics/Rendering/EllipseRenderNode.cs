using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class EllipseRenderNode(Rect rect, Brush.Resource? fill, Pen.Resource? pen)
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
                canvas.DrawEllipse(rect, fill, pen),
            fill: fill,
            pen: pen,
            outputBounds: bounds,
            hitTest: RenderHitTestContract.Custom(HitTest),
            scale: RenderScaleContract.Vector));
    }

    //https://github.com/AvaloniaUI/Avalonia/blob/release/0.10.21/src/Avalonia.Visuals/Rendering/SceneGraph/EllipseNode.cs
    private bool HitTest(RenderHitTestContext _, Point point)
    {
        Rect rect = Rect;
        Pen.Resource? pen = Pen?.Resource;
        float thickness = pen?.Thickness ?? 0;
        float realThickness = PenHelper.GetRealThickness(
            pen?.StrokeAlignment ?? StrokeAlignment.Center,
            thickness);

        Point center = rect.Center;

        float rx = rect.Width / 2 + realThickness;
        float ry = rect.Height / 2 + realThickness;

        float dx = point.X - center.X;
        float dy = point.Y - center.Y;

        if (Math.Abs(dx) > rx || Math.Abs(dy) > ry)
        {
            return false;
        }

        if (Fill?.Resource is not null)
        {
            return Contains(rx, ry);
        }
        else if (thickness > 0)
        {
            bool inStroke = Contains(rx, ry);

            rx = rect.Width / 2 - realThickness;
            ry = rect.Height / 2 - realThickness;

            bool inInner = Contains(rx, ry);

            return inStroke && !inInner;
        }

        bool Contains(double radiusX, double radiusY)
        {
            double rx2 = radiusX * radiusX;
            double ry2 = radiusY * radiusY;

            double distance = ry2 * dx * dx + rx2 * dy * dy;

            return distance <= rx2 * ry2;
        }

        return false;
    }
}
