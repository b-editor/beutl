using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Immutable;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using OpenCvSharp;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class Border : FilterEffect
{
    public static readonly CoreProperty<Point> OffsetProperty;
    public static readonly CoreProperty<int> ThicknessProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<MaskTypes> MaskTypeProperty;
    public static readonly CoreProperty<BorderStyles> StyleProperty;
    private Point _offset;
    private int _thickness;
    private Color _color;
    private MaskTypes _maskType;
    private BorderStyles _style;

    public enum MaskTypes
    {
        /// <summary>
        /// マスク処理をしない。
        /// </summary>
        None,

        /// <summary>
        /// マスク処理をする。
        /// </summary>
        Standard,

        /// <summary>
        /// マスク処理をする。(反転)
        /// </summary>
        Invert
    }

    public enum BorderStyles
    {
        /// <summary>
        /// ソースの後ろに縁取りを描画します。
        /// </summary>
        Background,

        /// <summary>
        /// ソースの表に縁取りを描画します。
        /// </summary>
        Foreground,
    }

    static Border()
    {
        OffsetProperty = ConfigureProperty<Point, Border>(nameof(Offset))
            .Accessor(o => o.Offset, (o, v) => o.Offset = v)
            .DefaultValue(default)
            .Register();

        ThicknessProperty = ConfigureProperty<int, Border>(nameof(Thickness))
            .Accessor(o => o.Thickness, (o, v) => o.Thickness = v)
            .DefaultValue(0)
            .Register();

        ColorProperty = ConfigureProperty<Color, Border>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.White)
            .Register();

        MaskTypeProperty = ConfigureProperty<MaskTypes, Border>(nameof(MaskType))
            .Accessor(o => o.MaskType, (o, v) => o.MaskType = v)
            .DefaultValue(MaskTypes.None)
            .Register();

        StyleProperty = ConfigureProperty<BorderStyles, Border>(nameof(Style))
            .Accessor(o => o.Style, (o, v) => o.Style = v)
            .DefaultValue(BorderStyles.Background)
            .Register();

        AffectsRender<Border>(
            OffsetProperty,
            ThicknessProperty,
            ColorProperty,
            MaskTypeProperty,
            StyleProperty);
    }

    [Display(Name = nameof(Strings.Offset), ResourceType = typeof(Strings))]
    public Point Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    [Display(Name = nameof(Strings.Thickness), ResourceType = typeof(Strings))]
    [Range(0, int.MaxValue)]
    public int Thickness
    {
        get => _thickness;
        set => SetAndRaise(ThicknessProperty, ref _thickness, value);
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    [Display(Name = nameof(Strings.MaskType), ResourceType = typeof(Strings))]
    public MaskTypes MaskType
    {
        get => _maskType;
        set => SetAndRaise(MaskTypeProperty, ref _maskType, value);
    }

    [Display(Name = nameof(Strings.BorderStyle), ResourceType = typeof(Strings))]
    public BorderStyles Style
    {
        get => _style;
        set => SetAndRaise(StyleProperty, ref _style, value);
    }

    public override Rect TransformBounds(Rect rect)
    {
        return rect.Union(rect.Translate(new Vector(_offset.X, _offset.Y)).Inflate(_thickness / 2)).Inflate(8);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Offset, Thickness, Color, MaskType, Style), Apply, TransformBounds);
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

            // 輪郭検出
            alphaMat.FindContours(
                out OpenCvSharp.Point[][] points,
                out HierarchyIndex[] h,
                RetrievalModes.List,
                ContourApproximationModes.ApproxSimple);

            // 検出した輪郭を描画
            borderMat.DrawContours(points, -1, new(color.B, color.G, color.R, color.A), thickness, LineTypes.AntiAlias, h);

            return border;
        }

        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            using SKImage skimage = target.Surface!.Value.Snapshot();
            using var src = skimage.ToBitmap();
            using var srcRef = Ref<IBitmap>.Create(src);
            using var srcBitmapSource = new BitmapSource(srcRef, "Temp");

            // 縁取りの画像を生成
            using Bitmap<Bgra8888> border = RenderBorderBitmap(src);
            var rect = target.Bounds;
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
                        canvas.DrawBitmap(src, Brushes.White, null);
                }

                float xx = -(offset.X - Math.Max(offset.X, thicknessHalf)) - thicknessHalf;
                float yy = -(offset.Y - Math.Max(offset.Y, thicknessHalf)) - thicknessHalf;

                using (canvas.PushTransform(Matrix.CreateTranslation(offset.X + xx, offset.Y + yy)))
                using (maskBrush != null ? canvas.PushOpacityMask(maskBrush, borderRect, maskType == MaskTypes.Invert) : new())
                {
                    canvas.DrawBitmap(border, Brushes.White, null);
                }

                if (style == BorderStyles.Background)
                {
                    using (canvas.PushTransform(srcTranslate))
                        canvas.DrawBitmap(src, Brushes.White, null);
                }
            }

            target.Dispose();
            context.Targets[i] = newTarget;
        }
    }
    /* Contours to SKPath
foreach (var inner in points)
{
    bool first = true;
    foreach (var item in inner)
    {
        if (first)
        {
            path.MoveTo(item.X, item.Y);
            first = false;
        }
        else
        {
            path.LineTo(item.X, item.Y);
        }
    }

    path.Close();
}
     */
}
