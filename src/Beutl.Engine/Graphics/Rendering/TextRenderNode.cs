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
        Rect bounds = text.ActualBounds;
        if (bounds.Width == 0 || bounds.Height == 0)
            return;

        (Brush.Resource Resource, int Version)? fillSnapshot = Fill;
        (Pen.Resource Resource, int Version)? penSnapshot = Pen;
        Brush.Resource? fill = fillSnapshot?.Resource;
        Pen.Resource? pen = penSnapshot?.Resource;
        Brush.Resource? textBrush = text.Brush;
        Pen.Resource? textPen = text.Pen;
        RenderResource<FormattedText> textResource = context.Borrow(
            text,
            DeferredOpaqueSource.GetCacheKey(text));
        RecordedPaint paint = BrushRecorder.RecordPaint(
            context,
            fill,
            fillSnapshot?.Version ?? 0,
            pen,
            penSnapshot?.Version ?? 0,
            bounds);
        RenderResource<Brush.Resource>? textBrushResource = textBrush is null
            ? null
            : context.Borrow(textBrush, BrushRecorder.GetResourceIdentity(textBrush), textBrush.Version);
        RenderResource<Pen.Resource>? textPenResource = textPen is null
            ? null
            : context.Borrow(textPen, BrushRecorder.GetResourceIdentity(textPen), textPen.Version);
        bool hasFill = fill is not null;

        OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
            execute: session => DeferredOpaqueSource.Execute(
                session,
                textResource,
                paint,
                static (canvas, currentText, currentFill, currentPen) =>
                    canvas.DrawText(currentText, currentFill, currentPen)),
            directReplay: session => DeferredOpaqueSource.ExecuteDirect(
                session,
                textResource,
                paint,
                static (canvas, currentText, currentFill, currentPen) =>
                    canvas.DrawText(currentText, currentFill, currentPen)),
            bounds: BrushRecorder.CreateSourceBounds(paint, bounds, typeof(TextRenderNode)),
            hitTest: RenderHitTestContract.FromResource(
                textResource,
                (currentText, point) => HitTest(currentText, hasFill, point),
                typeof(TextRenderNode)),
            scale: RenderScaleContract.Vector,
            structuralKey: typeof(TextRenderNode),
            runtimeIdentity: new RenderRuntimeIdentity(CreateRuntimeIdentity(text, bounds)),
            resources: DeferredOpaqueSource.Resources(
                [textResource, textBrushResource, textPenResource, .. paint.Resources]));
        context.Publish(BrushRecorder.RecordSource(context, paint, description));
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
            textBrush is null ? null : BrushRecorder.GetResourceIdentity(textBrush),
            textBrush?.Version,
            textPen is null ? null : BrushRecorder.GetResourceIdentity(textPen),
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
        object? BrushIdentity,
        int? BrushVersion,
        object? PenIdentity,
        int? PenVersion,
        Rect Bounds);
}
