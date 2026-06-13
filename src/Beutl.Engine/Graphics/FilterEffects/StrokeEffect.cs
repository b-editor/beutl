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
                // Anchor the source at its bounds origin WITHIN the (now symmetric) output box: origin = how far
                // the box top-left moved outward, so the source lands at target.Bounds.Position and the stroke at
                // +offset. For a plain pen (pen.Offset == 0) this reproduces the old (thickness, thickness)
                // placement byte-for-byte at Offset == 0. When pen.Offset > 0, PenHelper.GetBounds also inflates
                // by pen.Offset, so origin becomes (thickness + pen.Offset): the source is anchored at its bounds
                // instead of drifting by pen.Offset as the old code did — a deliberate fix (the source no longer
                // moves when only the pen offset changes), so it is NOT byte-identical to the old code there.
                var origin = Matrix.CreateTranslation(
                    target.Bounds.X - transformedBounds.X,
                    target.Bounds.Y - transformedBounds.Y);

                EffectTarget newTarget = context.CreateTarget(transformedBounds);
                // feature 003: Open bakes the base CTM CreateScale(density) for this ceil(bounds × wOut) buffer —
                // density read from the target, so an FR-037(b) clamp (an over-budget transformed bounds) is
                // honored. The contour path was already mapped to LOGICAL (÷ the input density w) above, so the
                // whole stroke draws in logical space and the base maps it to device at the (possibly clamped)
                // density without clipping. No manual prescale; density 1 stays byte-identical.
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
