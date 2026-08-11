using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public sealed class TextRenderNode(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    : BrushRenderNode(fill, pen)
{
    public FormattedText Text { get; private set; } = text;

    public bool Update(FormattedText text, Brush.Resource? fill, Pen.Resource? pen)
    {
        bool changed = Update(fill, pen);
        var oldText = Text;
        Text = text;
        if (changed || !oldText.Equals(text))
        {
            HasChanges = true;
            return true;
        }

        return false;
    }

    public override void Process(RenderNodeContext context)
    {
        FormattedText text = Text;
        Rect rasterBounds = text.GetRasterBounds(context.OutputScale);
        if (rasterBounds.Width == 0 || rasterBounds.Height == 0)
            return;

        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        Brush.Resource? textBrush = text.Brush;
        Pen.Resource? textPen = text.Pen;
        RenderResource<FormattedText> textResource = context.Borrow(text);
        RenderResource<Brush.Resource>? textBrushResource = textBrush is null
            ? null
            : context.Borrow(textBrush);
        RenderResource<Pen.Resource>? textPenResource = textPen is null
            ? null
            : context.Borrow(textPen);
        bool hasFill = fill is not null;

        context.Publish(context.PaintedSource(
            state: text,
            draw: static (canvas, fill, pen, state) =>
                canvas.DrawText(state, fill, pen),
            fill: fill,
            pen: pen,
            outputBounds: rasterBounds,
            hitTest: RenderHitTestContract.FromResource(
                textResource,
                (currentText, point) => HitTest(currentText, hasFill, point)),
            scale: RenderScaleContract.Vector,
            deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent,
            resources: DeferredOpaqueSource.Resources(
                textResource,
                textBrushResource,
                textPenResource)));
    }

    private static bool HitTest(FormattedText text, bool hasFill, Point point)
    {
        SKPath fill = text.GetFillPath();
        if (hasFill && fill.Contains(point.X, point.Y))
        {
            return true;
        }

        SKPath? stroke = text.GetStrokePath();
        return stroke?.Contains(point.X, point.Y) == true;
    }

}
