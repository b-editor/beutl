using BeUtl.Media;
using BeUtl.Media.Pixel;

using OpenCvSharp;

namespace BeUtl.Graphics.Effects;

public class Blur : BitmapEffect
{
    public static readonly CoreProperty<PixelSize> KernelSizeProperty;
    private PixelSize _kernelSize;

    static Blur()
    {
        KernelSizeProperty = ConfigureProperty<PixelSize, Blur>(nameof(KernelSize))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .DefaultValue(PixelSize.Empty)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .Register();

        AffectsRender<Blur>(KernelSizeProperty);
    }

    public Blur()
    {
        Processor = new _(this);
    }

    public PixelSize KernelSize
    {
        get => _kernelSize;
        set => SetAndRaise(KernelSizeProperty, ref _kernelSize, value);
    }

    public override IBitmapProcessor Processor { get; }

    public override Rect TransformBounds(Rect rect)
    {
        return rect.Inflate(new Thickness(0, 0, _kernelSize.Width, _kernelSize.Height));
    }

    private sealed class _ : IBitmapProcessor
    {
        private readonly Blur _blur;

        public _(Blur blur)
        {
            _blur = blur;
        }

        public void Process(in Bitmap<Bgra8888> src, out Bitmap<Bgra8888> dst)
        {
            int width = _blur._kernelSize.Width;
            int height = _blur._kernelSize.Height;
            Bitmap<Bgra8888> image = src.MakeBorder(src.Width + width, src.Height + height);

            using var mat = image.ToMat();
            if (width % 2 == 0)
            {
                width++;
            }

            if (height % 2 == 0)
            {
                height++;
            }

            Cv2.Blur(mat, mat, new(width, height));

            dst = image;
        }
    }
}
