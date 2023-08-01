using System.ComponentModel.DataAnnotations;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;

using OpenCvSharp;

using SkiaSharp;

namespace Beutl.Graphics.Effects.OpenCv;

public class GaussianBlur : FilterEffect
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
            .Register();

        SigmaProperty = ConfigureProperty<Vector, GaussianBlur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Vector.Zero)
            .Register();

        FixImageSizeProperty = ConfigureProperty<bool, GaussianBlur>(nameof(FixImageSize))
            .Accessor(o => o.FixImageSize, (o, v) => o.FixImageSize = v)
            .DefaultValue(false)
            .Register();

        AffectsRender<GaussianBlur>(KernelSizeProperty, SigmaProperty, FixImageSizeProperty);
    }

    [Display(Name = nameof(Strings.KernelSize), ResourceType = typeof(Strings))]
    public PixelSize KernelSize
    {
        get => _kernelSize;
        set => SetAndRaise(KernelSizeProperty, ref _kernelSize, value);
    }

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    public Vector Sigma
    {
        get => _sigma;
        set => SetAndRaise(SigmaProperty, ref _sigma, value);
    }

    [Display(Name = nameof(Strings.FixImageSize), ResourceType = typeof(Strings))]
    public bool FixImageSize
    {
        get => _fixImageSize;
        set => SetAndRaise(FixImageSizeProperty, ref _fixImageSize, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Custom((KernelSize, Sigma, FixImageSize), Apply, TransformBounds);
    }

    private static Rect TransformBounds((PixelSize KernelSize, Vector Sigma, bool FixImageSize) data, Rect rect)
    {
        if (!data.FixImageSize)
        {
            int halfWidth = data.KernelSize.Width / 2;
            int halfHeight = data.KernelSize.Height / 2;
            rect = rect.Inflate(new Thickness(halfWidth, halfHeight));
        }
        return rect;
    }

    private static void Apply((PixelSize KernelSize, Vector Sigma, bool FixImageSize) data, FilterEffectCustomOperationContext context)
    {
        if (context.Target.Surface is { } surface)
        {
            int kwidth = data.KernelSize.Width;
            int kheight = data.KernelSize.Height;
            if (kwidth % 2 == 0)
                kwidth++;
            if (kheight % 2 == 0)
                kheight++;

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
                        dst = src.MakeBorder(src.Width + kwidth, src.Height + kheight);
                    }
                }

                using var mat = dst.ToMat();
                Cv2.GaussianBlur(mat, mat, new(kwidth, kheight), data.Sigma.X, data.Sigma.Y);

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
        return TransformBounds((KernelSize, Sigma, FixImageSize), rect);
    }
}
