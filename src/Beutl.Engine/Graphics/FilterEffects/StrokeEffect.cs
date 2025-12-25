using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using SkiaSharp;
using Cv = OpenCvSharp;

namespace Beutl.Graphics.Effects;

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

    [Display(Name = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public IProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    [Display(Name = nameof(Strings.BorderStyle), ResourceType = typeof(Strings))]
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
        static SKPath CreateBorderPath(Bitmap<Bgra8888> src)
        {
            using Cv.Mat srcMat = src.ToMat();
            using Cv.Mat alphaMat = srcMat.ExtractChannel(3);

            alphaMat.FindContours(
                out Cv.Point[][] points,
                out Cv.HierarchyIndex[] h,
                Cv.RetrievalModes.List,
                Cv.ContourApproximationModes.ApproxSimple);

            var skpath = new SKPath();
            foreach (Cv.Point[] inner in points)
            {
                bool first = true;
                foreach (Cv.Point item in inner)
                {
                    if (first)
                    {
                        skpath.MoveTo(item.X, item.Y);
                        first = false;
                    }
                    else
                    {
                        skpath.LineTo(item.X, item.Y);
                    }
                }

                skpath.Close();
            }

            return skpath;
        }

        if (data.Pen is {  } pen)
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                EffectTarget target = context.Targets[i];
                RenderTarget srcRenderTarget = target.RenderTarget!;
                using var src = srcRenderTarget.Snapshot();

                using SKPath borderPath = CreateBorderPath(src);

                Rect transformedBounds = TransformBounds(data, target.Bounds);
                float thickness = PenHelper.GetRealThickness(pen.StrokeAlignment, pen.Thickness);
                var origin = Matrix.CreateTranslation(
                    thickness - Math.Min(data.Offset.X, thickness),
                    thickness - Math.Min(data.Offset.Y, thickness));

                EffectTarget newTarget = context.CreateTarget(transformedBounds);
                using (ImmediateCanvas newCanvas = context.Open(newTarget))
                using (newCanvas.PushTransform(origin))
                {
                    newCanvas.Clear();
                    if (data.Style == StrokeStyles.Background)
                    {
                        newCanvas.DrawRenderTarget(srcRenderTarget, default);
                    }

                    using (newCanvas.PushTransform(Matrix.CreateTranslation(data.Offset.X, data.Offset.Y)))
                    {
                        newCanvas.DrawSKPath(borderPath, true, null, pen);
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
