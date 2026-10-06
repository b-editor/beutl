using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
public partial class StrokeEffect : FilterEffect
{
    public enum StrokeStyles
    {
        Background,
        Foreground,
    }

    public StrokeEffect()
    {
        ScanProperties<StrokeEffect>();
        Pen.CurrentValue = new Pen();
    }

    [Display(Name = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    [Display(Name = nameof(GraphicsStrings.Offset), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    [Display(Name = nameof(GraphicsStrings.StrokeEffect_Style), ResourceType = typeof(GraphicsStrings))]
    public IProperty<StrokeStyles> Style { get; } = Property.CreateAnimatable(StrokeStyles.Background);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect(
            (r.Offset, r.Pen, r.Style),
            Apply,
            TransformBounds);
    }

    private static Rect TransformBounds((Point Offset, Pen.Resource? Pen, StrokeStyles Style) data, Rect rect)
    {
        Rect borderBounds = PenHelper.GetBounds(rect, data.Pen);
        // Inflate symmetrically by offset so the source stays centered.
        return borderBounds.Inflate(new Thickness(
            Math.Abs(data.Offset.X), Math.Abs(data.Offset.Y),
            Math.Abs(data.Offset.X), Math.Abs(data.Offset.Y)));
    }

    private static void Apply((Point Offset, Pen.Resource? Pen, StrokeStyles Style) data, CustomFilterEffectContext context)
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

                using (ImmediateCanvas newCanvas = context.Open(newTarget))
                using (newCanvas.PushTransform(origin))
                {
                    newCanvas.Clear();
                    if (data.Style == StrokeStyles.Background)
                    {
                        target.Draw(newCanvas);
                    }

                    using (newCanvas.PushTransform(Matrix.CreateTranslation(data.Offset.X, data.Offset.Y)))
                    {
                        newCanvas.DrawSKPath(borderPath, true, null, pen);
                    }

                    if (data.Style == StrokeStyles.Foreground)
                    {
                        target.Draw(newCanvas);
                    }
                }

                target.Dispose();
                context.Targets[i] = newTarget;
            }
        }
    }
}
