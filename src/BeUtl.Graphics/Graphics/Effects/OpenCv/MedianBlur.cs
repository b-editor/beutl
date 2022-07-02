using BeUtl.Media;
using BeUtl.Media.Pixel;

using OpenCvSharp;

namespace BeUtl.Graphics.Effects.OpenCv;

public class MedianBlur : BitmapEffect
{
    public static readonly CoreProperty<int> KernelSizeProperty;
    public static readonly CoreProperty<bool> FixImageSizeProperty;
    private int _kernelSize;
    private bool _fixImageSize;

    static MedianBlur()
    {
        KernelSizeProperty = ConfigureProperty<int, MedianBlur>(nameof(KernelSize))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .DefaultValue(0)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("kernel-size")
            .Register();

        FixImageSizeProperty = ConfigureProperty<bool, MedianBlur>(nameof(FixImageSize))
            .Accessor(o => o.FixImageSize, (o, v) => o.FixImageSize = v)
            .DefaultValue(false)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .SerializeName("fix-image-size")
            .Register();

        AffectsRender<MedianBlur>(KernelSizeProperty, FixImageSizeProperty);
    }

    public MedianBlur()
    {
        Processor = new _(this);
    }

    public int KernelSize
    {
        get => _kernelSize;
        set => SetAndRaise(KernelSizeProperty, ref _kernelSize, value);
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
            rect = rect.Inflate(new Thickness(0, 0, _kernelSize, _kernelSize));
        }

        return rect;
    }

    private sealed class _ : IBitmapProcessor
    {
        private readonly MedianBlur _blur;

        public _(MedianBlur blur)
        {
            _blur = blur;
        }

        public void Process(in Bitmap<Bgra8888> src, out Bitmap<Bgra8888> dst)
        {
            int ksize = _blur._kernelSize;
            Bitmap<Bgra8888>? image;
            if (_blur.FixImageSize)
            {
                image = (Bitmap<Bgra8888>)src.Clone();
            }
            else
            {
                image = src.MakeBorder(src.Width + ksize, src.Height + ksize);
            }

            using var mat = image.ToMat();
            if (ksize % 2 == 0)
            {
                ksize++;
            }

            Cv2.MedianBlur(mat, mat, ksize);

            dst = image;
        }
    }
}
