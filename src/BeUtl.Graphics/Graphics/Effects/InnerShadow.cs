using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.Media.Pixel;

using OpenCvSharp;

namespace BeUtl.Graphics.Effects;

public class InnerShadow : BitmapEffect
{
    public static readonly CoreProperty<Point> PositionProperty;
    public static readonly CoreProperty<PixelSize> KernelSizeProperty;
    public static readonly CoreProperty<Color> ColorProperty;
    private Point _position;
    private PixelSize _kernelSize;
    private Color _color;

    static InnerShadow()
    {
        PositionProperty = ConfigureProperty<Point, InnerShadow>(nameof(Position))
            .Accessor(o => o.Position, (o, v) => o.Position = v)
            .DefaultValue(new Point())
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("position")
            .Register();

        KernelSizeProperty = ConfigureProperty<PixelSize, InnerShadow>(nameof(KernelSize))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("kernel-size")
            .Register();

        ColorProperty = ConfigureProperty<Color, InnerShadow>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Transparent)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("color")
            .Register();

        AffectsRender<InnerShadow>(PositionProperty, KernelSizeProperty, ColorProperty);
    }

    public InnerShadow()
    {
        Processor = new _(this);
    }

    public Point Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    public PixelSize KernelSize
    {
        get => _kernelSize;
        set => SetAndRaise(KernelSizeProperty, ref _kernelSize, value);
    }

    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    public override IBitmapProcessor Processor { get; }

    private sealed class _ : IBitmapProcessor
    {
        private readonly InnerShadow _shadow;

        public _(InnerShadow shadow)
        {
            _shadow = shadow;
        }

        public void Process(in Bitmap<Bgra8888> src, out Bitmap<Bgra8888> dst)
        {
            var rect = new Rect(0, 0, src.Width, src.Height);
            PixelSize ksize = _shadow.KernelSize;

            using var canvas = new Canvas(src.Width, src.Height);
            var maskBrush = new ImageBrush(src)
            {
                Transform = new TranslateTransform(_shadow._position),
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
                Stretch = Stretch.None,
                TileMode = TileMode.None
            };
            using (canvas.PushOpacityMask(maskBrush, rect, true))
            {
                canvas.Clear(_shadow.Color);
            }

            using Bitmap<Bgra8888> blurred = canvas.GetBitmap();
            using (Mat blurredMat = blurred.ToMat())
            {
                Cv2.Blur(blurredMat, blurredMat, new OpenCvSharp.Size(ksize.Width, ksize.Height));
            }

            maskBrush.Transform = null;
            canvas.Clear();
            canvas.DrawBitmap(src);
            using (canvas.PushOpacityMask(maskBrush, rect, false))
            {
                canvas.DrawBitmap(blurred);
            }

            dst = canvas.GetBitmap();
        }
    }
}
