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

        Rect bounds = text.Bounds;

        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        Brush.Resource? textBrush = text.Brush;
        Pen.Resource? textPen = text.Pen;
        RenderResource<FormattedText> textResource = context.Borrow(
            text,
            DeferredOpaqueSource.GetCacheKey(text));
        RenderResource<Brush.Resource>? textBrushResource = textBrush is null
            ? null
            : context.Borrow(textBrush, EngineResourceIdentity.Of(textBrush), textBrush.Version);
        RenderResource<Pen.Resource>? textPenResource = textPen is null
            ? null
            : context.Borrow(textPen, EngineResourceIdentity.Of(textPen), textPen.Version);
        bool hasFill = fill is not null;
        TextRuntimeIdentity runtimeIdentity = CreateRuntimeIdentity(text, bounds);

        context.Publish(context.PaintedSource(
            state: (text, runtimeIdentity),
            draw: static (canvas, fill, pen, state) =>
                canvas.DrawText(state.text, fill, pen),
            fill: fillSnapshot,
            pen: penSnapshot,
            outputBounds: rasterBounds,
            hitTest: RenderHitTestContract.FromResource(
                textResource,
                (currentText, point) => HitTest(currentText, hasFill, point),
                typeof(TextRenderNode)),
            scale: RenderScaleContract.Vector,
            structuralKey: typeof(TextRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity(runtimeIdentity),
            deviceGridSensitivity: RenderDeviceGridSensitivity.PhaseDependent,
            resources: DeferredOpaqueSource.Resources(
                ("text", textResource),
                ("textBrush", textBrushResource),
                ("textPen", textPenResource))));
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

    private static TextRuntimeIdentity CreateRuntimeIdentity(FormattedText text, Rect bounds)
    {
        Brush.Resource? textBrush = text.Brush;
        Pen.Resource? textPen = text.Pen;
        return new TextRuntimeIdentity(
            text.Weight,
            text.Style,
            text.Font.Name,
            text.Size,
            text.Spacing,
            text.Text,
            text.BeginOnNewLine,
            textBrush is null ? null : EngineResourceIdentity.Of(textBrush),
            textBrush?.Version,
            textPen is null ? null : EngineResourceIdentity.Of(textPen),
            textPen?.Version,
            bounds);
    }

    private readonly record struct TextRuntimeIdentity(
        FontWeight Weight,
        FontStyle Style,
        string FontFamily,
        float Size,
        float Spacing,
        StringSpan Text,
        bool BeginOnNewLine,
        Guid? BrushIdentity,
        int? BrushVersion,
        Guid? PenIdentity,
        int? PenVersion,
        Rect Bounds);
}
