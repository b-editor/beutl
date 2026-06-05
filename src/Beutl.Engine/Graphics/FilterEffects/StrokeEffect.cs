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

    // feature 003 (FR-013): resolution-sensitive effect — keep a high source's density through it.
    public override Beutl.Graphics.Rendering.ResolutionPolicy ResolutionPolicy
        => Beutl.Graphics.Rendering.ResolutionPolicy.PreserveSource;

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
        // Keep the output box CENTERED on the source. Inflate the stroked bounds symmetrically by the offset
        // magnitude instead of unioning with the one-directional translated border: the union grows the box
        // only toward the offset, leaving the source pinned to the opposite corner (it "sticks to the
        // top-left" for a positive offset, which reads as the object jumping to the corner of its selection
        // box in the editor). Symmetric inflation keeps the source centered while still enclosing the
        // offset stroke. Offset == 0 → borderBounds, byte-identical to the previous behavior.
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

                // feature 003: the contour path + source are DEVICE px. Map the path to LOGICAL (× 1/w) and prescale
                // the canvas by w, so the logical pen width / offsets / origin map to device at working density, and
                // draw the source via the scale-aware target.Draw. w == 1 keeps the pre-feature path (byte-identical).
                float w = context.WorkingScale;
                using SKPath borderPath = CreateBorderPath(src);
                if (w != 1f) borderPath.Transform(SKMatrix.CreateScale(1f / w, 1f / w));

                Rect transformedBounds = TransformBounds(data, target.Bounds);
                // Place the source at its original position WITHIN the (now symmetric) output box: the box
                // top-left moved out by thickness + |offset| on each side, so the source origin shifts by the
                // same amount. This keeps the rendered source / stroke at the exact same absolute positions as
                // before — only the box is re-centered. Offset == 0 → (thickness, thickness), unchanged.
                var origin = Matrix.CreateTranslation(
                    target.Bounds.X - transformedBounds.X,
                    target.Bounds.Y - transformedBounds.Y);

                EffectTarget newTarget = context.CreateTarget(transformedBounds);
                using (ImmediateCanvas newCanvas = context.Open(newTarget))
                using (w == 1f ? default : newCanvas.PushTransform(Matrix.CreateScale(w, w)))
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
