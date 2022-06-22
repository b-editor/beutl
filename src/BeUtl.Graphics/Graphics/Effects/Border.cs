using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Media;
using BeUtl.Media.Pixel;

using OpenCvSharp;

namespace BeUtl.Graphics.Effects;

public class Border : BitmapEffect
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
            .DefaultValue(default(Point))
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        ThicknessProperty = ConfigureProperty<int, Border>(nameof(Thickness))
            .Accessor(o => o.Thickness, (o, v) => o.Thickness = v)
            .DefaultValue(0)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        ColorProperty = ConfigureProperty<Color, Border>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.White)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        MaskTypeProperty = ConfigureProperty<MaskTypes, Border>(nameof(MaskType))
            .Accessor(o => o.MaskType, (o, v) => o.MaskType = v)
            .DefaultValue(MaskTypes.None)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        StyleProperty = ConfigureProperty<BorderStyles, Border>(nameof(Style))
            .Accessor(o => o.Style, (o, v) => o.Style = v)
            .DefaultValue(BorderStyles.Background)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        AffectsRender<Border>(
            OffsetProperty,
            ThicknessProperty,
            ColorProperty,
            MaskTypeProperty,
            StyleProperty);
    }

    public Border()
    {
        Processor = new _(this);
    }

    public Point Offset
    {
        get => _offset;
        set => SetAndRaise(OffsetProperty, ref _offset, value);
    }

    public int Thickness
    {
        get => _thickness;
        set => SetAndRaise(ThicknessProperty, ref _thickness, value);
    }

    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    public MaskTypes MaskType
    {
        get => _maskType;
        set => SetAndRaise(MaskTypeProperty, ref _maskType, value);
    }

    public BorderStyles Style
    {
        get => _style;
        set => SetAndRaise(StyleProperty, ref _style, value);
    }

    public override IBitmapProcessor Processor { get; }

    public override Rect TransformBounds(Rect rect)
    {
        return rect.Union(rect.Translate(_offset).Inflate(_thickness));
    }

    private sealed class _ : IBitmapProcessor
    {
        private readonly Border _border;

        public _(Border border)
        {
            _border = border;
        }

        public void Process(in Bitmap<Bgra8888> src, out Bitmap<Bgra8888> dst)
        {
            Point offset = _border._offset;
            int thickness = _border._thickness;
            MaskTypes maskType = _border._maskType;
            BorderStyles style = _border._style;
            using Bitmap<Bgra8888> border = GetBorderBitmap(src);
            var rect = new Rect(0, 0, src.Width, src.Height);
            var borderBounds = rect.Translate(offset).Inflate(thickness);
            var bounds1 = rect.Union(borderBounds);
            var canvasSize = bounds1.Size;

            ImageBrush? maskBrush = maskType != MaskTypes.None ? new ImageBrush(src) : null;
            if (maskBrush != null)
            {
                maskBrush.Stretch = Stretch.None;
            }

            var bounds = new Rect(0, 0, border.Width, border.Height);

            using (var canvas = new Canvas((int)canvasSize.Width, (int)canvasSize.Height))
            {
                canvas.Clear(Colors.Gray);

                // 最低でもthickness分の移動量は打ち消す、
                var translate = Matrix.CreateTranslation(
                    thickness - Math.Min(offset.X, thickness),
                    thickness - Math.Min(offset.Y, thickness));
                var translate2 = Matrix.CreateTranslation((canvas.Size.Width - border.Width) / 2, (canvas.Size.Height - border.Height) / 2);
                if (style == BorderStyles.Foreground)
                {
                    using (canvas.PushTransform(translate))
                        canvas.DrawBitmap(src);
                }

                //using (maskBrush != null ? canvas.PushOpacityMask(maskBrush, bounds, maskType == MaskTypes.Invert) : new())
                //using (canvas.PushTransform(translate2.Append(Matrix.CreateTranslation(offset))))
                //{
                //    canvas.DrawBitmap(border);
                //}

                if (style == BorderStyles.Background)
                {
                    using (canvas.PushTransform(translate))
                        canvas.DrawBitmap(src);
                }

                dst = canvas.GetBitmap();
            }
        }

        private Bitmap<Bgra8888> GetBorderBitmap(Bitmap<Bgra8888> src)
        {
            Color col = _border._color;
            int size = _border._thickness;
            int newWidth = src.Width + (size * 2);
            int newHeight = src.Height + (size * 2);

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
            borderMat.DrawContours(points, -1, new(col.B, col.G, col.R, col.A), size, LineTypes.AntiAlias, h);

            return border;
        }
    }
}
