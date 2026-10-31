using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Serialization;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
public partial class StrokeEffect : FilterEffect
{
    public StrokeEffect()
    {
        ScanProperties<StrokeEffect>();
        Pen.Value = new Pen();
    }

    [Display(Name = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Pen?> Pen { get; } = Property.CreateExpression<Pen?>();

    [Display(Name = nameof(GraphicsStrings.Offset), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        // The pen's stroke order replaced a Style member, saved as 0 for drawing the stroke over the content and 1
        // for drawing it under. Only its value carries over: the stroke order is not animatable.
        if (context is IJsonSerializationContext jsonContext
            && jsonContext.GetNode("Style") is JsonValue style
            && style.TryGetValue(out int value)
            && value == 1
            && Pen.Value is { } pen)
        {
            pen.StrokeOrder.Value = StrokeOrder.BelowFill;
        }
    }

    private static Rect TransformBounds((Point Offset, Pen.Resource? Pen) data, Rect rect)
    {
        Rect borderBounds = PenHelper.GetBounds(rect, data.Pen);
        // Inflate symmetrically by offset so the source stays centered.
        return borderBounds.Inflate(new Thickness(
            Math.Abs(data.Offset.X), Math.Abs(data.Offset.Y),
            Math.Abs(data.Offset.X), Math.Abs(data.Offset.Y)));
    }

    private static void Apply((Point Offset, Pen.Resource? Pen) data, CustomFilterEffectContext context)
    {
        if (data.Pen is { } pen)
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                EffectTarget target = context.Targets[i];
                RenderTarget srcRenderTarget = target.RenderTarget!;
                using Bitmap src = srcRenderTarget.SnapshotAlpha();

                // The contour path is device px; map to logical (/ w) for logical pen width/offset.
                float w = context.WorkingScale;
                using SKPath borderPath = ContourPaths.CreateOutline(src);
                if (w != 1f) borderPath.Transform(SKMatrix.CreateScale(1f / w, 1f / w));

                Rect transformedBounds = TransformBounds(data, target.Bounds);
                var origin = Matrix.CreateTranslation(
                    target.Bounds.X - transformedBounds.X,
                    target.Bounds.Y - transformedBounds.Y);

                EffectTarget newTarget = context.CreateTarget(transformedBounds);
                if (newTarget.IsEmpty)
                {
                    newTarget.Dispose();
                    continue;
                }

                // The content is the fill the stroke outlines, so the pen's stroke order decides which goes on top.
                bool strokeBeneath = PenHelper.DrawsBeneathFill(pen);
                using (ImmediateCanvas newCanvas = context.Open(newTarget))
                using (newCanvas.PushTransform(origin))
                {
                    newCanvas.Clear();
                    if (!strokeBeneath)
                    {
                        target.Draw(newCanvas);
                    }

                    using (newCanvas.PushTransform(Matrix.CreateTranslation(data.Offset.X, data.Offset.Y)))
                    {
                        newCanvas.DrawSKPath(borderPath, pen);
                    }

                    if (strokeBeneath)
                    {
                        target.Draw(newCanvas);
                    }
                }

                context.Targets[i] = newTarget;
            }
        }
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.CustomEffect(
                (Offset, Pen),
                Apply,
                TransformBounds);
        }
    }
}
