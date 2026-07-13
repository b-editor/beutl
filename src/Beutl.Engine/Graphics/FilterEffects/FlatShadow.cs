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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var data = (r.Angle, r.Length, r.Brush, r.ShadowOnly);
        // Forward is the legacy TransformBounds (the extrusion expands the bounds). ApplyGeometry snapshots and
        // contour-traces the WHOLE materialized input (ContourTracer.FindContours), so backward must claim the entire
        // input regardless of the requested output region: a band r ∪ (r − extrusionVector) is insufficient because a
        // cropped snapshot yields false/truncated contours (the crop edge becomes a spurious silhouette), exactly as
        // for StrokeEffect. An under-claiming backward crops the upstream and corrupts the tracing.
        Rect inputBounds = builder.Bounds;
        builder.Geometry(GeometryNodeDescriptor.Create(
            session => ApplyGeometry(session, data),
            BoundsContract.Create(rect => TransformBounds(data, rect), _ => inputBounds),
            structuralToken: nameof(FlatShadow), requiresReadback: true));
    }

    private static void ApplyGeometry(
        GeometrySession session, (float Angle, float Length, Brush.Resource? Brush, bool ShadowOnly) data)
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

        EffectInput input = session.Inputs[0];
        ImmediateCanvas newCanvas = session.OpenCanvas();
        Brush.Resource? brush = data.Brush;
        float length = data.Length;
        float radian = MathUtilities.Deg2Rad(data.Angle);

        using Bitmap srcBitmap = input.Snapshot();

        float x1 = MathF.Cos(radian);
        float y1 = MathF.Sin(radian);
        float x2 = length * x1;
        float y2 = length * y1;
        float x2Abs = Math.Abs(x2);
        float y2Abs = Math.Abs(y2);

        // The output buffer occupies session.Bounds (the expanded rect from TransformBounds). The path and shadow are
        // built in the input's device px (from its snapshot), then bridged to the output density.
        float w = input.Density.IsUnbounded ? 1f : input.Density.Value;
        float wOut = session.WorkingScale;

        // The device-space math below is authored relative to the un-cropped OutputBounds origin. A downstream
        // deflating pass can ROI-crop this pass so session.Bounds is an OFFSET sub-rect of that; bridge the origin
        // (like Clipping) so content still registers to the actual buffer. Zero when un-cropped (golden parity).
        Rect outputBounds = TransformBounds(data, input.Bounds);
        float bridgeX = (float)(outputBounds.X - session.Bounds.X) * wOut;
        float bridgeY = (float)(outputBounds.Y - session.Bounds.Y) * wOut;
        bool bridged = bridgeX != 0 || bridgeY != 0;

        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var brushPaint = new SKPaint();
        using SKPath path = CreatePath(srcBitmap);

        using (newCanvas.PushDeviceSpace())
        using (bridged ? newCanvas.PushTransform(Matrix.CreateTranslation(bridgeX, bridgeY)) : default)
        using (w == wOut ? default : newCanvas.PushTransform(Matrix.CreateScale(wOut / w, wOut / w)))
        using (newCanvas.PushTransform(Matrix.CreateTranslation((x2Abs - x2) / 2 * w, (y2Abs - y2) / 2 * w)))
        {
            float lenAbs = Math.Abs(length) * w;
            int unit = Math.Sign(length);
            for (int i = 0; i < lenAbs; i++)
            {
                newCanvas.Transform = Matrix.CreateTranslation(x1 * unit, y1 * unit) * newCanvas.Transform;
                newCanvas.Canvas.DrawPath(path, paint);
            }
        }

        // The brush anchors to the un-cropped output frame, not the (possibly ROI-cropped) session.Bounds sub-rect —
        // a gradient/image brush would otherwise re-anchor into the crop. The fill rides the same origin bridge as
        // the silhouette draws (in logical units here; the base CTM applies wOut), and SrcIn clips it to the shadow.
        var c = new BrushConstructor(
            new(outputBounds.Size), brush, BlendMode.SrcIn, wOut, session.MaxWorkingScale, session.Diagnostics);
        c.ConfigurePaint(brushPaint);
        using (bridged
            ? newCanvas.PushTransform(Matrix.CreateTranslation(
                outputBounds.X - session.Bounds.X, outputBounds.Y - session.Bounds.Y))
            : default)
        {
            newCanvas.Canvas.DrawRect(SKRect.Create(outputBounds.Width, outputBounds.Height), brushPaint);
        }

        if (!data.ShadowOnly)
        {
            using (newCanvas.PushDeviceSpace())
            using (bridged ? newCanvas.PushTransform(Matrix.CreateTranslation(bridgeX, bridgeY)) : default)
            using (w == wOut ? default : newCanvas.PushTransform(Matrix.CreateScale(wOut / w, wOut / w)))
            {
                input.Draw(newCanvas, new Point((x2Abs - x2) / 2 * w, (y2Abs - y2) / 2 * w));
            }
        }
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
}
