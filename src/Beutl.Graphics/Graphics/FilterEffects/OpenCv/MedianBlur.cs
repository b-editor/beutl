using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;

using OpenCvSharp;

using SkiaSharp;

namespace Beutl.Graphics.Effects.OpenCv;

public class MedianBlur : FilterEffect
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
            .Register();

        FixImageSizeProperty = ConfigureProperty<bool, MedianBlur>(nameof(FixImageSize))
            .Accessor(o => o.FixImageSize, (o, v) => o.FixImageSize = v)
            .DefaultValue(false)
            .Register();

        AffectsRender<MedianBlur>(KernelSizeProperty, FixImageSizeProperty);
    }

    [Display(Name = nameof(Strings.KernelSize), ResourceType = typeof(Strings))]
    public int KernelSize
    {
        get => _kernelSize;
        set => SetAndRaise(KernelSizeProperty, ref _kernelSize, value);
    }

    [Display(Name = nameof(Strings.FixImageSize), ResourceType = typeof(Strings))]
    public bool FixImageSize
    {
        get => _fixImageSize;
        set => SetAndRaise(FixImageSizeProperty, ref _fixImageSize, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Custom((KernelSize, FixImageSize), Apply, TransformBounds);
    }

    private static Rect TransformBounds((int KernelSize, bool FixImageSize) data, Rect rect)
    {
        if (!data.FixImageSize)
        {
            rect = rect.Inflate(new Thickness(0, 0, data.KernelSize, data.KernelSize));
        }

        return rect;
    }

    private static void Apply((int KernelSize, bool FixImageSize) data, FilterEffectCustomOperationContext context)
    {
        if (context.Target.Surface is { } surface)
        {
            int ksize = data.KernelSize;
            if (ksize % 2 == 0)
                ksize++;

            Bitmap<Bgra8888>? dst = null;

            try
            {
                using (SKImage skimage = surface.Value.Snapshot())
                using (var src = skimage.ToBitmap())
                {
                    if (data.FixImageSize)
                    {
                        dst = src.Clone();
                    }
                    else
                    {
                        dst = src.MakeBorder(src.Width + ksize, src.Height + ksize);
                    }
                }

                using var mat = dst.ToMat();
                Cv2.MedianBlur(mat, mat, ksize);

                using EffectTarget target = context.CreateTarget(dst.Width, dst.Height);
                target.Surface!.Value.Canvas.DrawBitmap(dst.ToSKBitmap(), 0, 0);
                context.ReplaceTarget(target);
            }
            finally
            {
                dst?.Dispose();
            }
        }
    }

    public override Rect TransformBounds(Rect rect)
    {
        if (!_fixImageSize)
        {
            rect = rect.Inflate(new Thickness(0, 0, _kernelSize, _kernelSize));
        }

        return rect;
    }
}
