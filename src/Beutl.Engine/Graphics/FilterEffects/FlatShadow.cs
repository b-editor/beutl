using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
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
        Brush.CurrentValue = new SolidColorBrush(Colors.Gray);
    }

    [Display(Name = nameof(GraphicsStrings.Angle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Angle { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.FlatShadow_Length), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Length { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.Brush), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.ShadowOnly), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> ShadowOnly { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect((r.Angle, r.Length, r.Brush, r.ShadowOnly), Apply, TransformBounds);
    }

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
        static SKPath CreatePath(Bitmap src)
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

        Brush.Resource? brush = data.Brush;
        float length = data.Length;
        float radian = MathUtilities.Deg2Rad(data.Angle);

        for (int ii = 0; ii < context.Targets.Count; ii++)
        {
            var target = context.Targets[ii];
            using var srcBitmap = target.RenderTarget!.Snapshot();

            float x1 = MathF.Cos(radian);
            float y1 = MathF.Sin(radian);
            float x2 = length * x1;
            float y2 = length * y1;
            float x2Abs = Math.Abs(x2);
            float y2Abs = Math.Abs(y2);

            Size size = target.Bounds.Size;
            EffectTarget newTarget = context.CreateTarget(
                new Rect(
                    target.Bounds.X - (x2Abs - x2) / 2,
                    target.Bounds.Y - (y2Abs - y2) / 2,
                    (size.Width + x2Abs),
                    (size.Height + y2Abs)));
            using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill })
            using (var brushPaint = new SKPaint())
            using (SKPath path = CreatePath(srcBitmap))
            using (ImmediateCanvas newCanvas = context.Open(newTarget))
            {
                newCanvas.Clear();
                // feature 003: the contour path is DEVICE px (traced from the ceil(bounds × w) source), so the
                // shadow offset, the per-step extrusion COUNT and the source-blit offset all scale to device by
                // the working density to stay consistent with it. The SrcIn brush is built over the LOGICAL
                // bounds at density w and its rect is drawn under a Scale(w) CTM — the same device coverage as
                // a W·w×H·w rect under identity, but absolute-unit gradient points and the Perlin frequency stay
                // logical, and tile/image/drawable content rasterizes at w. w == 1 = pre-feature path.
                float w = context.WorkingScale;
                // feature 003 (FR-037(b)): if CreateTarget clamped the EXPANDED output buffer below the working
                // scale, scale the whole device-px shadow construction (contour + extrusion + source, built at the
                // input density w; the SrcIn brush rides its own Scale(w)) by wOut/w so all of it maps onto the
                // smaller wOut buffer at once — no clip, registration preserved, just lower resolution (the clamp's
                // intended tradeoff). wOut == w (the common, unclamped case) is a no-op (byte-identical).
                float wOut = newTarget.Scale.Value;
                using (w == wOut ? default : newCanvas.PushTransform(Matrix.CreateScale(wOut / w, wOut / w)))
                {
                    using (newCanvas.PushTransform(Matrix.CreateTranslation((x2Abs - x2) / 2 * w, (y2Abs - y2) / 2 * w)))
                    {
                        var c = new BrushConstructor(new(newTarget.Bounds.Size), brush, BlendMode.SrcIn, w,
                            context.MaxWorkingScale);
                        c.ConfigurePaint(brushPaint);

                        float lenAbs = Math.Abs(length) * w;
                        int unit = Math.Sign(length);
                        for (int i = 0; i < lenAbs; i++)
                        {
                            newCanvas.Transform = Matrix.CreateTranslation(x1 * unit, y1 * unit) * newCanvas.Transform;
                            newCanvas.Canvas.DrawPath(path, paint);
                        }
                    }

                    using (w == 1f ? default : newCanvas.PushTransform(Matrix.CreateScale(w, w)))
                    {
                        newCanvas.Canvas.DrawRect(SKRect.Create(newTarget.Bounds.Width, newTarget.Bounds.Height), brushPaint);
                    }

                    if (!data.ShadowOnly)
                        newCanvas.DrawRenderTarget(target.RenderTarget!, new((x2Abs - x2) / 2 * w, (y2Abs - y2) / 2 * w));
                }
            }

            target.Dispose();
            context.Targets[ii] = newTarget;
        }
    }
}
