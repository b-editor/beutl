using Beutl.Media;
using Beutl.Media.Pixel;

using OpenCvSharp;

namespace Beutl.Graphics.Effects.OpenCv;

public class GaussianBlur : BitmapEffect
{
    public static readonly CoreProperty<PixelSize> KernelSizeProperty;
    public static readonly CoreProperty<Vector> SigmaProperty;
    public static readonly CoreProperty<bool> FixImageSizeProperty;
    private PixelSize _kernelSize;
    private Vector _sigma;
    private bool _fixImageSize;

    static GaussianBlur()
    {
        KernelSizeProperty = ConfigureProperty<PixelSize, GaussianBlur>(nameof(KernelSize))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .DefaultValue(PixelSize.Empty)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("kernel-size")
            .Register();

        SigmaProperty = ConfigureProperty<Vector, GaussianBlur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Vector.Zero)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("sigma")
            .Register();

        FixImageSizeProperty = ConfigureProperty<bool, GaussianBlur>(nameof(FixImageSize))
            .Accessor(o => o.FixImageSize, (o, v) => o.FixImageSize = v)
            .DefaultValue(false)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("fix-image-size")
            .Register();

        AffectsRender<GaussianBlur>(KernelSizeProperty, SigmaProperty, FixImageSizeProperty);
    }

    public GaussianBlur()
    {
        Processor = new _(this);
    }

    public PixelSize KernelSize
    {
        get => _kernelSize;
        set => SetAndRaise(KernelSizeProperty, ref _kernelSize, value);
    }

    public Vector Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    public bool FixImageSize
    {
        get => _fixImageSize;
        set => SetAndRaise(FixImageSizeProperty, ref _fixImageSize, value);
    }

    public override IBitmapProcessor Processor { get; }

    public override Rect TransformBounds(Rect rect)
    {
        if (!_fixImageSize)
        {
            rect = rect.Inflate(new Thickness(0, 0, _kernelSize.Width, _kernelSize.Height));
        }

        return rect;
    }

    private sealed class _ : IBitmapProcessor
    {
        private readonly GaussianBlur _blur;

        public _(GaussianBlur blur)
        {
            _blur = blur;
        }

        public void Process(in Bitmap<Bgra8888> src, out Bitmap<Bgra8888> dst)
        {
            int width = _blur._kernelSize.Width;
            int height = _blur._kernelSize.Height;
            float sigmaX = _blur._sigma.X;
            float sigmaY = _blur._sigma.Y;

            Bitmap<Bgra8888>? image;
            if (_blur.FixImageSize)
            {
                image = (Bitmap<Bgra8888>)src.Clone();
            }
            else
            {
                image = src.MakeBorder(src.Width + width, src.Height + height);
            }

            using var mat = image.ToMat();
            if (width % 2 == 0)
            {
                width++;
            }

            if (height % 2 == 0)
            {
                height++;
            }

            Cv2.GaussianBlur(mat, mat, new(width, height), sigmaX, sigmaY);

            dst = image;
        }
    }
}
