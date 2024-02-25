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
        context.CustomEffect((KernelSize, FixImageSize), Apply, TransformBounds);
    }

    private static Rect TransformBounds((int KernelSize, bool FixImageSize) data, Rect rect)
    {
        if (!data.FixImageSize)
        {
            int ksize = data.KernelSize;
            if (ksize % 2 == 0)
                ksize++;

            int half = ksize / 2;
            rect = rect.Inflate(half);
        }
        return rect;
    }

    private static void Apply((int KernelSize, bool FixImageSize) data, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            var surface = target.Surface!;
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

                EffectTarget newtarget = context.CreateTarget(TransformBounds(data, target.Bounds));
                newtarget.Surface!.Value.Canvas.DrawBitmap(dst.ToSKBitmap(), 0, 0);
                target.Dispose();
                context.Targets[i] = newtarget;
            }
            finally
            {
                dst?.Dispose();
            }
        }
    }

    public override Rect TransformBounds(Rect rect)
    {
        return TransformBounds((KernelSize, FixImageSize), rect);
    }
}
