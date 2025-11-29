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

        return changed;
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(
                PenHelper.GetBounds(Rect, Pen?.Resource),
                canvas => canvas.DrawEllipse(Rect, Fill?.Resource, Pen?.Resource),
                HitTest
            )
        ];
    }

    //https://github.com/AvaloniaUI/Avalonia/blob/release/0.10.21/src/Avalonia.Visuals/Rendering/SceneGraph/EllipseNode.cs
    private bool HitTest(Point point)
    {
        Point center = Rect.Center;

        float thickness = Pen?.Resource.Thickness ?? 0;
        StrokeAlignment alignment = Pen?.Resource.StrokeAlignment ?? StrokeAlignment.Center;
        float realThickness = PenHelper.GetRealThickness(alignment, thickness);

        float rx = Rect.Width / 2 + realThickness;
        float ry = Rect.Height / 2 + realThickness;

        float dx = point.X - center.X;
        float dy = point.Y - center.Y;

        if (Math.Abs(dx) > rx || Math.Abs(dy) > ry)
        {
            return false;
        }

        if (Fill != null)
        {
            return Contains(rx, ry);
        }
        else if (thickness > 0)
        {
            bool inStroke = Contains(rx, ry);

            rx = Rect.Width / 2 - realThickness;
            ry = Rect.Height / 2 - realThickness;

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
