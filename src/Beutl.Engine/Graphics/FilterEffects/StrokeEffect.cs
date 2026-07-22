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
            for (int i = 0; i < context.Targets.Count; i++)
            {
                EffectTarget target = context.Targets[i];
                RenderTarget srcRenderTarget = target.RenderTarget!;
                using var src = srcRenderTarget.Snapshot();

                // The contour path is in backing-buffer pixels. Map it with the target's actual
                // density and physical raster origin so it shares EffectTarget.Draw's placement.
                float w = target.Scale.Value;
                using SKPath borderPath = CreateBorderPath(src);
                if (w != 1f) borderPath.Transform(SKMatrix.CreateScale(1f / w, 1f / w));
                Vector rasterOrigin = target.RasterBounds.Position - target.Bounds.Position;
                if (rasterOrigin != default)
                {
                    borderPath.Transform(SKMatrix.CreateTranslation(
                        (float)rasterOrigin.X,
                        (float)rasterOrigin.Y));
                }

                Rect transformedBounds = TransformBounds(data, target.Bounds);
                var origin = Matrix.CreateTranslation(
                    target.Bounds.X - transformedBounds.X,
                    target.Bounds.Y - transformedBounds.Y);

                EffectTarget newTarget = context.CreateTarget(transformedBounds);
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
