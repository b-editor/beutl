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
        return rect.Union(borderBounds.Translate(new Vector(data.Offset.X, data.Offset.Y)));
    }

    private static void Apply((Point Offset, Pen.Resource? Pen, StrokeStyles Style) data, CustomFilterEffectContext context)
    {
        static SKPath CreateBorderPath(Bitmap src)
        {
            using var contours = ContourTracer.FindContours(src);

            var skpath = new SKPath();
            foreach (var contour in contours)
            {
                for (int j = 0; j < contour.Count; j++)
                {
                    if (j == 0)
                        skpath.MoveTo(contour[j].X, contour[j].Y);
                    else
                        skpath.LineTo(contour[j].X, contour[j].Y);
                }

                skpath.Close();
            }

            return skpath;
        }

        if (data.Pen is { } pen)
        {
            // New EffectTargets and src renderTargets are at upstream raster scale (proxy physical).
            // Contour coords come back in physical pixels of the upstream snapshot; for `DrawSKPath`
            // to honour `pen.Thickness` as an authoring-space value we render the path under a
            // Scale(1/scale) matrix, which forces Skia to interpret both the path and the pen's
            // stroke width in authoring units. To compose with that, the contour path is lifted into
            // authoring coords by pre-multiplying its pixel coords by upstream CorrectionScale.
            var scale = context.CorrectionScale;
            float invSx = scale.IsIdentity ? 1f : 1f / scale.ScaleX;
            float invSy = scale.IsIdentity ? 1f : 1f / scale.ScaleY;

            for (int i = 0; i < context.Targets.Count; i++)
            {
                EffectTarget target = context.Targets[i];
                RenderTarget srcRenderTarget = target.RenderTarget!;
                using var src = srcRenderTarget.Snapshot();

                using SKPath borderPath = CreateBorderPath(src);
                if (!scale.IsIdentity)
                {
                    borderPath.Transform(SKMatrix.CreateScale(scale.ScaleX, scale.ScaleY));
                }

                Rect transformedBounds = TransformBounds(data, target.Bounds);
                float thickness = PenHelper.GetRealThickness(pen.StrokeAlignment, pen.Thickness);
                // origin / offset translations are authored; divide per-axis so they map to physical
                // pixels of the new RT (which is at upstream raster scale).
                var origin = Matrix.CreateTranslation(
                    (thickness - Math.Min(data.Offset.X, thickness)) * invSx,
                    (thickness - Math.Min(data.Offset.Y, thickness)) * invSy);

                EffectTarget newTarget = context.CreateTarget(transformedBounds);
                using (ImmediateCanvas newCanvas = context.Open(newTarget))
                using (newCanvas.PushTransform(origin))
                {
                    newCanvas.Clear();
                    if (data.Style == StrokeStyles.Background)
                    {
                        newCanvas.DrawRenderTarget(srcRenderTarget, default);
                    }

                    using (newCanvas.PushTransform(Matrix.CreateTranslation(data.Offset.X * invSx, data.Offset.Y * invSy)))
                    {
                        if (scale.IsIdentity)
                        {
                            newCanvas.DrawSKPath(borderPath, true, null, pen);
                        }
                        else
                        {
                            // Path is in authoring coords; PushTransform(Scale(1/scale)) maps authoring
                            // → physical raster pixels of the new RT. Skia evaluates `pen.Thickness`
                            // in the current canvas's unit system, so it picks up the same Scale →
                            // stroke width visually equals `pen.Thickness` authoring units after the
                            // compositor's final upscale.
                            using (newCanvas.PushTransform(Matrix.CreateScale(invSx, invSy)))
                            {
                                newCanvas.DrawSKPath(borderPath, true, null, pen);
                            }
                        }
                    }

                    if (data.Style == StrokeStyles.Foreground)
                    {
                        newCanvas.DrawRenderTarget(srcRenderTarget, default);
                    }
                }

                target.Dispose();
                context.Targets[i] = newTarget;
            }
        }
    }
}
