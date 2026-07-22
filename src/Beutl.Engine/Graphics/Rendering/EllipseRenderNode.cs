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

        RecordedPaint paint = BrushRecorder.RecordPaint(
            context,
            fill,
            fillSnapshot?.Version ?? 0,
            pen,
            penSnapshot?.Version ?? 0,
            bounds);
        var hitTestState = new EllipseHitTestState(
            rect,
            fill is not null,
            pen?.StrokeAlignment ?? StrokeAlignment.Center,
            pen?.Thickness ?? 0);

        OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
            execute: session => DeferredOpaqueSource.Execute(
                session,
                paint,
                (canvas, currentFill, currentPen) =>
                    canvas.DrawEllipse(rect, currentFill, currentPen)),
            directReplay: session => DeferredOpaqueSource.ExecuteDirect(
                session,
                paint,
                (canvas, currentFill, currentPen) =>
                    canvas.DrawEllipse(rect, currentFill, currentPen)),
            bounds: BrushRecorder.CreateSourceBounds(paint, bounds, typeof(EllipseRenderNode)),
            hitTest: RenderHitTestContract.Custom(
                (_, point) => hitTestState.HitTest(point),
                typeof(EllipseRenderNode)),
            scale: RenderScaleContract.Vector,
            structuralKey: typeof(EllipseRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity((rect, hitTestState)),
            resources: paint.Resources);
        context.Publish(BrushRecorder.RecordSource(context, paint, description));
    }

    //https://github.com/AvaloniaUI/Avalonia/blob/release/0.10.21/src/Avalonia.Visuals/Rendering/SceneGraph/EllipseNode.cs
    private readonly record struct EllipseHitTestState(
        Rect Rect,
        bool HasFill,
        StrokeAlignment StrokeAlignment,
        float Thickness)
    {
        public bool HitTest(Point point)
        {
            Point center = Rect.Center;

            float realThickness = PenHelper.GetRealThickness(StrokeAlignment, Thickness);

            float rx = Rect.Width / 2 + realThickness;
            float ry = Rect.Height / 2 + realThickness;

            float dx = point.X - center.X;
            float dy = point.Y - center.Y;

            if (Math.Abs(dx) > rx || Math.Abs(dy) > ry)
            {
                return false;
            }

            if (HasFill)
            {
                return Contains(rx, ry);
            }
            else if (Thickness > 0)
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
}
