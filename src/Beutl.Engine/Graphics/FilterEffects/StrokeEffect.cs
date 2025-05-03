using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;

using SkiaSharp;

using Cv = OpenCvSharp;

namespace Beutl.Graphics.Effects;

public class StrokeEffect : FilterEffect
{
    public static readonly CoreProperty<IPen?> PenProperty;
    public static readonly CoreProperty<Point> OffsetProperty;
    public static readonly CoreProperty<StrokeStyles> StyleProperty;
    private IPen? _pen;
    private Point _offset;
    private StrokeStyles _style;

    public enum StrokeStyles
    {
        Background,
        Foreground,
    }

    static StrokeEffect()
    {
        PenProperty = ConfigureProperty<IPen?, StrokeEffect>(nameof(Pen))
            .Accessor(o => o.Pen, (o, v) => o.Pen = v)
            .Register();

        OffsetProperty = ConfigureProperty<Point, StrokeEffect>(nameof(Offset))
            .Accessor(o => o.Offset, (o, v) => o.Offset = v)
            .DefaultValue(default)
            .Register();

        StyleProperty = ConfigureProperty<StrokeStyles, StrokeEffect>(nameof(Style))
            .Accessor(o => o.Style, (o, v) => o.Style = v)
            .DefaultValue(StrokeStyles.Background)
            .Register();

        AffectsRender<StrokeEffect>(
            PenProperty,
            OffsetProperty,
            StyleProperty);
    }

    public StrokeEffect()
    {
        // コンストラクタで初期化しないと、AffectsRenderが購読されない
        Pen = new Pen();
    }

    [Display(Name = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IPen? Pen
    {
        get => _pen;
        set => SetAndRaise(PenProperty, ref _pen, value);
    }

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public Point Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    [Display(Name = nameof(Strings.BorderStyle), ResourceType = typeof(Strings))]
    public StrokeStyles Style
    {
        get => _style;
        set => SetAndRaise(StyleProperty, ref _style, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Pen as IAnimatable)?.ApplyAnimations(clock);
    }

    public override Rect TransformBounds(Rect rect)
    {
        Rect borderBounds = PenHelper.GetBounds(rect, Pen);
        return rect.Union(borderBounds.Translate(new Vector(Offset.X, Offset.Y)));
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Offset, (Pen as IMutablePen)?.ToImmutable(), Style), Apply, TransformBounds);
    }

    private static Rect TransformBounds((Point Offset, IPen? Pen, StrokeStyles Style) data, Rect rect)
    {
        Rect borderBounds = PenHelper.GetBounds(rect, data.Pen);
        return rect.Union(borderBounds.Translate(new Vector(data.Offset.X, data.Offset.Y)));
    }

    private static void Apply((Point Offset, IPen? Pen, StrokeStyles Style) data, CustomFilterEffectContext context)
    {
        static SKPath CreateBorderPath(Bitmap<Bgra8888> src)
        {
            using Cv.Mat srcMat = src.ToMat();
            using Cv.Mat alphaMat = srcMat.ExtractChannel(3);

            // 輪郭検出
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

        if (data.Pen is IPen pen)
        {
            for (int i = 0; i < context.Targets.Count; i++)
            {
                EffectTarget target = context.Targets[i];
                RenderTarget srcRenderTarget = target.RenderTarget!;
                using var src = srcRenderTarget.Snapshot();

                // 縁取りのパスを作成
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
                    // 縁取りの後ろに描画
                    if (data.Style == StrokeStyles.Background)
                        newCanvas.DrawRenderTarget(srcRenderTarget, default);

                    // 縁取り描画
                    using (newCanvas.PushTransform(Matrix.CreateTranslation(data.Offset.X, data.Offset.Y)))
                    {
                        newCanvas.DrawSKPath(borderPath, true, null, pen);
                    }

                    // 縁取りの表に描画
                    if (data.Style == StrokeStyles.Foreground)
                        newCanvas.DrawRenderTarget(srcRenderTarget, default);

                }

                target.Dispose();
                context.Targets[i] = newTarget;
            }
        }
    }
}
