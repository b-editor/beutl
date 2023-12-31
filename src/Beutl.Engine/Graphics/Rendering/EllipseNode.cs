using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class EllipseNode(Rect rect, IBrush? fill, IPen? pen)
    : BrushDrawNode(fill, pen, PenHelper.GetBounds(rect, pen))
{
    public Rect Rect { get; } = rect;

    public bool Equals(Rect rect, IBrush? fill, IPen? pen)
    {
        return Rect == rect
            && EqualityComparer<IBrush?>.Default.Equals(Fill, fill)
            && EqualityComparer<IPen?>.Default.Equals(Pen, pen);
    }

    public override void Render(ImmediateCanvas canvas)
    {
        canvas.DrawEllipse(Rect, Fill, Pen);
    }

    //https://github.com/AvaloniaUI/Avalonia/blob/release/0.10.21/src/Avalonia.Visuals/Rendering/SceneGraph/EllipseNode.cs
    public override bool HitTest(Point point)
    {
        Point center = Rect.Center;

        float thickness = Pen?.Thickness ?? 0;
        StrokeAlignment alignment = Pen?.StrokeAlignment ?? StrokeAlignment.Center;
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
