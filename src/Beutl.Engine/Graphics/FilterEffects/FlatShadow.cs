using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Utilities;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.FlatShadow), ResourceType = typeof(GraphicsStrings))]
public partial class FlatShadow : FilterEffect
{
    public FlatShadow()
    {
        ScanProperties<FlatShadow>();
        Brush.Value = new SolidColorBrush(Colors.Gray);
    }

    [Display(Name = nameof(GraphicsStrings.Angle), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Angle { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.FlatShadow_Length), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Length { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Brush), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Brush?> Brush { get; } = Property.CreateExpression<Brush?>();

    [Display(Name = nameof(GraphicsStrings.ShadowOnly), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> ShadowOnly { get; } = Property.CreateAnimatable(false);

    private static Rect TransformBounds((float Angle, float Length, Brush.Resource? Brush, bool ShadowOnly) data,
        Rect rect)
    {
        float length = data.Length;
        float radian = MathUtilities.Deg2Rad(data.Angle);
        float x = length * MathF.Cos(radian);
        float y = length * MathF.Sin(radian);
        float xAbs = Math.Abs(x);
        float yAbs = Math.Abs(y);

        float width = rect.Width + xAbs;
        float height = rect.Height + yAbs;

        return new Rect(rect.X - (xAbs - x) / 2, rect.Y - (yAbs - y) / 2, width, height);
    }

    private static void Apply((float Angle, float Length, Brush.Resource? Brush, bool ShadowOnly) data,
        CustomFilterEffectContext context)
    {
        Brush.Resource? brush = data.Brush;
        float length = data.Length;
        float radian = MathUtilities.Deg2Rad(data.Angle);
        float x1 = MathF.Cos(radian);
        float y1 = MathF.Sin(radian);
        float x2 = length * x1;
        float y2 = length * y1;
        float x2Abs = Math.Abs(x2);
        float y2Abs = Math.Abs(y2);
        int unit = Math.Sign(length);

        for (int ii = 0; ii < context.Targets.Count; ii++)
        {
            var target = context.Targets[ii];
            EffectTarget newTarget = context.CreateTarget(TransformBounds(data, target.Bounds));
            if (newTarget.IsEmpty)
            {
                newTarget.Dispose();
                continue;
            }

            // The outline is in device px, so the shadow is extruded in device space.
            float w = context.WorkingScale;
            float lenAbs = Math.Abs(length) * w;
            using SKPath? shadow = lenAbs > 0 && float.IsFinite(lenAbs)
                ? CreateShadowPath(target.RenderTarget!, x1 * unit, y1 * unit, lenAbs)
                : null;

            using (var brushPaint = new SKPaint())
            using (ImmediateCanvas newCanvas = context.Open(newTarget))
            {
                newCanvas.Clear();
                // CreateTarget may clamp below w; compensate with Scale(wOut / w).
                float wOut = newTarget.Scale.Value;

                if (shadow != null)
                {
                    using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
                    using (newCanvas.PushDeviceSpace())
                    using (w == wOut ? default : newCanvas.PushTransform(Matrix.CreateScale(wOut / w, wOut / w)))
                    using (newCanvas.PushTransform(Matrix.CreateTranslation((x2Abs - x2) / 2 * w, (y2Abs - y2) / 2 * w)))
                    {
                        newCanvas.Canvas.DrawPath(shadow, paint);
                    }
                }

                // SrcIn brush at the buffer's real density (wOut).
                context.CreateBrushConstructor(
                    new Rect(newTarget.Bounds.Size),
                    brush,
                    BlendMode.SrcIn,
                    wOut).ConfigurePaint(brushPaint);
                newCanvas.Canvas.DrawRect(SKRect.Create(newTarget.Bounds.Width, newTarget.Bounds.Height), brushPaint);

                if (!data.ShadowOnly)
                {
                    using (newCanvas.PushDeviceSpace())
                    using (w == wOut ? default : newCanvas.PushTransform(Matrix.CreateScale(wOut / w, wOut / w)))
                    {
                        newCanvas.DrawRenderTarget(target.RenderTarget!,
                            new((x2Abs - x2) / 2 * w, (y2Abs - y2) / 2 * w));
                    }
                }
            }

            context.Targets[ii] = newTarget;
        }
    }

    // The region the content covers as it moves along the unit vector (dx, dy) from one device pixel out to length, in
    // the source's device px. One fill draws it, however long the shadow is.
    private static SKPath CreateShadowPath(RenderTarget source, float dx, float dy, float length)
    {
        using Bitmap alpha = source.SnapshotAlpha();
        IReadOnlyList<Point[]> outlines = AlphaOutlineTracer.Trace(alpha);

        // The shadow starts a pixel out, even when it is shorter than that: any nearer, it would show through the
        // content's anti-aliased edge on the side facing away, which a length growing from zero would flash for a frame.
        var origin = new Vector(dx, dy);
        float travel = MathF.Max(length - 1, 0);
        var sweep = new Vector(dx * travel, dy * travel);

        using var builder = new SKPathBuilder();
        foreach (Point[] outline in outlines)
            ContourPaths.AddClosedPolyline(builder, outline, origin);

        // A point the moving content reaches is first reached by an edge that faces the way it moves, so the content at
        // its first position and the strips those edges sweep make up the whole shadow. The non-zero fill unites them.
        foreach (Point[] outline in outlines)
            AddSweptRuns(builder, outline, origin, sweep);

        return builder.Detach();
    }

    // Adds the strip each run of consecutive edges facing the sweep passes over.
    private static void AddSweptRuns(SKPathBuilder builder, Point[] outline, Vector origin, Vector sweep)
    {
        int count = outline.Length;
        // Start after an edge that does not face the sweep, so no run wraps past the end. A closed outline has one, as
        // its edges add up to nothing.
        int first = -1;
        for (int i = 0; i < count; i++)
        {
            if (!Faces(outline[i], outline[(i + 1) % count], sweep))
            {
                first = i + 1;
                break;
            }
        }

        if (first < 0)
            return;

        int k = 0;
        while (k < count)
        {
            int from = first + k;
            while (k < count && Faces(outline[(first + k) % count], outline[(first + k + 1) % count], sweep))
                k++;

            if (first + k > from)
                AddStrip(builder, outline, from, first + k, origin, sweep);

            k++;
        }
    }

    // Outlines run clockwise on screen and holes counter-clockwise, so the content lies to the right of each edge, and
    // an edge faces the sweep when the sweep points to its left.
    private static bool Faces(Point p, Point q, Vector sweep)
        => (q.X - p.X) * sweep.Y - (q.Y - p.Y) * sweep.X < 0;

    // The strip the outline's points from..to (indices wrapping past the end) pass over. Along a run the points advance
    // across the sweep in one direction, so the strip does not fold over itself, and it runs clockwise on screen like
    // the outlines.
    private static void AddStrip(SKPathBuilder builder, Point[] outline, int from, int to, Vector origin, Vector sweep)
    {
        int count = outline.Length;
        Point p = outline[from % count];
        builder.MoveTo(p.X + origin.X, p.Y + origin.Y);
        for (int i = from; i <= to; i++)
        {
            p = outline[i % count];
            builder.LineTo(p.X + origin.X + sweep.X, p.Y + origin.Y + sweep.Y);
        }

        for (int i = to; i > from; i--)
        {
            p = outline[i % count];
            builder.LineTo(p.X + origin.X, p.Y + origin.Y);
        }

        builder.Close();
    }

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.CustomEffect((Angle, Length, Brush, ShadowOnly), Apply, TransformBounds);
        }
    }
}
