using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Immutable;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using OpenCvSharp;

namespace Beutl.Graphics.Effects;

[Obsolete("Use StrokeEffect instead.")]
public class Border : FilterEffect
{
    public enum MaskTypes
    {
        None,
        Standard,
        Invert
    }

    public enum BorderStyles
    {
        Background,
        Foreground,
    }

    public Border()
    {
        ScanProperties<Border>();
    }

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public IProperty<Point> Offset { get; } = Property.CreateAnimatable(default(Point));

    [Display(Name = nameof(Strings.Thickness), ResourceType = typeof(Strings))]
    [Range(0, int.MaxValue)]
    public IProperty<int> Thickness { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(Strings.MaskType), ResourceType = typeof(Strings))]
    public IProperty<MaskTypes> MaskType { get; } = Property.CreateAnimatable(MaskTypes.None);

    [Display(Name = nameof(Strings.BorderStyle), ResourceType = typeof(Strings))]
    public IProperty<BorderStyles> Style { get; } = Property.CreateAnimatable(BorderStyles.Background);

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Offset.CurrentValue, Thickness.CurrentValue, Color.CurrentValue, MaskType.CurrentValue, Style.CurrentValue), Apply, TransformBounds);
    }

    private static Rect TransformBounds((Point Offset, int Thickness, Color Color, MaskTypes MaskType, BorderStyles Style) data, Rect rect)
    {
        return rect.Union(rect.Translate(new Vector(data.Offset.X, data.Offset.Y)).Inflate(data.Thickness / 2)).Inflate(8);
    }

    private static void Apply((Point Offset, int Thickness, Color Color, MaskTypes MaskType, BorderStyles Style) data, CustomFilterEffectContext context)
    {
        Point offset = data.Offset;
        Color color = data.Color;
        int thickness = data.Thickness;
        int thicknessHalf = thickness / 2;
        MaskTypes maskType = data.MaskType;
        BorderStyles style = data.Style;

        Bitmap<Bgra8888> RenderBorderBitmap(Bitmap<Bgra8888> src)
        {
            int newWidth = src.Width + thickness;
            int newHeight = src.Height + thickness;

            using Bitmap<Bgra8888> src2 = src.MakeBorder(newWidth, newHeight);

            using Mat srcMat = src2.ToMat();
            using Mat alphaMat = srcMat.ExtractChannel(3);

            var border = new Bitmap<Bgra8888>(newWidth, newHeight);
            using Mat borderMat = border.ToMat();

            alphaMat.FindContours(
                out OpenCvSharp.Point[][] points,
                out HierarchyIndex[] h,
                RetrievalModes.List,
                ContourApproximationModes.ApproxSimple);

            borderMat.DrawContours(points, -1, new(color.B, color.G, color.R, color.A), thickness, LineTypes.AntiAlias, h);

            return border;
        }

        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            using var src = target.RenderTarget!.Snapshot();
            using var srcRef = Ref<IBitmap>.Create(src);
            using var srcBitmapSource = new BitmapSource(srcRef, "Temp");

            using Bitmap<Bgra8888> border = RenderBorderBitmap(src);
            Rect rect = target.Bounds;
            Rect borderBounds = rect.Translate(offset).Inflate(thicknessHalf);
            Rect canvasRect = rect.Union(borderBounds).Inflate(8);
            var borderRect = new Rect(0, 0, border.Width, border.Height);

            ImmutableImageBrush? maskBrush = null;
            if (maskType != MaskTypes.None)
            {
                maskBrush = new ImmutableImageBrush(srcBitmapSource, stretch: Stretch.None);
            }

            EffectTarget newTarget = context.CreateTarget(canvasRect);
            using (ImmediateCanvas canvas = context.Open(newTarget))
            using (canvas.PushTransform(Matrix.CreateTranslation(8, 8)))
            {
                var srcTranslate = Matrix.CreateTranslation(
                    thicknessHalf - Math.Min(offset.X, thicknessHalf),
                    thicknessHalf - Math.Min(offset.Y, thicknessHalf));
                if (style == BorderStyles.Foreground)
                {
                    using (canvas.PushTransform(srcTranslate))
                    {
                        canvas.DrawBitmap(src, Brushes.White, null);
                    }
                }

                float xx = -(offset.X - Math.Max(offset.X, thicknessHalf)) - thicknessHalf;
                float yy = -(offset.Y - Math.Max(offset.Y, thicknessHalf)) - thicknessHalf;

                /* PushOpacityMask
                   PUSH:
                   var paint = new SKPaint();

                   int count = Canvas.SaveLayer(paint);
                   new BrushConstructor(bounds, mask, (BlendMode)paint.BlendMode).ConfigurePaint(paint);

                   POP:
                   canvas._sharedFillPaint.Reset();
                   canvas._sharedFillPaint.BlendMode = Invert ? SKBlendMode.DstOut : SKBlendMode.DstIn;

                   canvas.Canvas.SaveLayer(canvas._sharedFillPaint);
                   using (SKPaint maskPaint = Paint)
                   {
                       canvas.Canvas.DrawPaint(maskPaint);
                   }

                   canvas.Canvas.Restore();

                   canvas.Canvas.RestoreToCount(Count);
                 */

                using (canvas.PushTransform(Matrix.CreateTranslation(offset.X + xx, offset.Y + yy)))
                using (maskBrush != null ? canvas.PushOpacityMask(maskBrush, borderRect, maskType == MaskTypes.Invert) : new())
                {
                    canvas.DrawBitmap(border, Brushes.White, null);
                }

                if (style == BorderStyles.Background)
                {
                    using (canvas.PushTransform(srcTranslate))
                    {
                        canvas.DrawBitmap(src, Brushes.White, null);
                    }
                }
            }

            target.Dispose();
            context.Targets[i] = newTarget;
        }
    }
}
