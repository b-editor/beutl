using System.ComponentModel.DataAnnotations;

using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;

using OpenCvSharp;

namespace Beutl.Graphics.Effects;

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
            .Register();

        KernelSizeProperty = ConfigureProperty<PixelSize, InnerShadow>(nameof(KernelSize))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .DefaultValue(new PixelSize())
            .Register();

        ColorProperty = ConfigureProperty<Color, InnerShadow>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .DefaultValue(Colors.Transparent)
            .Register();

        AffectsRender<InnerShadow>(PositionProperty, KernelSizeProperty, ColorProperty);
    }

    public InnerShadow()
    {
        Processor = new _(this);
    }

    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public Point Position
    {
        get => _position;
        set => SetAndRaise(PositionProperty, ref _position, value);
    }

    [Display(Name = nameof(Strings.KernelSize), ResourceType = typeof(Strings))]
    [Range(typeof(PixelSize), "1,1", "max,max")]
    public PixelSize KernelSize
    {
        get => _kernelSize;
        set => SetAndRaise(KernelSizeProperty, ref _kernelSize, value);
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
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
            var maskSource = new ImageSource(Ref<IBitmap>.Create(src), "Temp");
            var maskBrush = new ImageBrush(maskSource)
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
