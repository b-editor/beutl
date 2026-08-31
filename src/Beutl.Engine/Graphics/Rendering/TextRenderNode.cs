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
            MarkChanged();
            return true;
        }

        return false;
    }

    public override void Process(RenderNodeContext context)
    {
        FormattedText text = Text;
        Rect actualBounds = text.ActualBounds;
        // The mask decides emptiness: a glyph can have a degenerate outline and still rasterize something.
        Rect rasterBounds = text.GetRasterBounds(context.OutputScale).Union(actualBounds);
        if (rasterBounds.Width == 0 || rasterBounds.Height == 0)
            return;

        // The bounds a fragment publishes are what place it, so they have to be the text's own. Hinting moves
        // the glyph masks off that rectangle by a couple of logical units, and by a different amount at every
        // density, so the room the masks need is declared as buffer-only room instead: publishing it would
        // shift the composition whenever the preview scale or the export scale changed. A degenerate outline
        // leaves no scale-independent rectangle to place by, so the mask's own footprint is all there is.
        Rect bounds = actualBounds.Width > 0 && actualBounds.Height > 0 ? actualBounds : rasterBounds;
        var rasterOutset = new Thickness(
            (float)(bounds.Left - rasterBounds.Left),
            (float)(bounds.Top - rasterBounds.Top),
            (float)(rasterBounds.Right - bounds.Right),
            (float)(rasterBounds.Bottom - bounds.Bottom));

        Brush.Resource? fill = Fill?.Resource;
        Pen.Resource? pen = Pen?.Resource;
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
            outputBounds: bounds,
            hitTest: RenderHitTestContract.FromResource(
                textResource,
                hasFill,
                static (text, hasFill, point) => HitTest(text, hasFill, point)),
            scale: RenderScaleContract.Vector,
            directReplayAtExactIntegerReduction: false,
            deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent,
            resources: DeferredOpaqueSource.Resources(
                textResource,
                textBrushResource,
                textPenResource),
            rasterOutset: rasterOutset));
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
