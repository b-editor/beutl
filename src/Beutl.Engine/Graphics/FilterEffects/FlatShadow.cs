using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
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

    [Display(Name = nameof(GraphicsStrings.FlatShadow_Angle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Angle { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.FlatShadow_Length), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Length { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.FlatShadow_Brush), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> Brush { get; } = Property.Create<Brush?>();

    [Display(Name = nameof(GraphicsStrings.FlatShadow_ShadowOnly), ResourceType = typeof(GraphicsStrings))]
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
        static SKPath CreatePath(Bitmap<Bgra8888> src)
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
                using (newCanvas.PushTransform(Matrix.CreateTranslation((x2Abs - x2) / 2, (y2Abs - y2) / 2)))
                {
                    var c = new BrushConstructor(new(newTarget.Bounds.Size), brush, BlendMode.SrcIn);
                    c.ConfigurePaint(brushPaint);

                    float lenAbs = Math.Abs(length);
                    int unit = Math.Sign(length);
                    for (int i = 0; i < lenAbs; i++)
                    {
                        newCanvas.Transform = Matrix.CreateTranslation(x1 * unit, y1 * unit) * newCanvas.Transform;
                        newCanvas.Canvas.DrawPath(path, paint);
                    }
                }

                newCanvas.Canvas.DrawRect(SKRect.Create(newTarget.Bounds.Size.ToSKSize()), brushPaint);

                if (!data.ShadowOnly)
                    newCanvas.DrawRenderTarget(target.RenderTarget!, new((x2Abs - x2) / 2, (y2Abs - y2) / 2));
            }

            target.Dispose();
            context.Targets[ii] = newTarget;
        }
    }
}
