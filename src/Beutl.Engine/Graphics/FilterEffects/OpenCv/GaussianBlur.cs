using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using OpenCvSharp;

namespace Beutl.Graphics.Effects.OpenCv;

[Display(Name = "CvGaussianBlur")]
public partial class GaussianBlur : FilterEffect
{
    public GaussianBlur()
    {
        ScanProperties<GaussianBlur>();
    }

    [Display(Name = nameof(Strings.KernelSize), ResourceType = typeof(Strings))]
    [Range(typeof(PixelSize), "0,0", "max,max")]
    public IProperty<PixelSize> KernelSize { get; } = Property.CreateAnimatable(PixelSize.Empty);

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    [Display(Name = nameof(Strings.FixImageSize), ResourceType = typeof(Strings))]
    public IProperty<bool> FixImageSize { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect((r.KernelSize, r.Sigma, r.FixImageSize), Apply, TransformBounds);
    }

    private static Rect TransformBounds((PixelSize KernelSize, Size Sigma, bool FixImageSize) data, Rect rect)
    {
        if (!data.FixImageSize)
        {
            int kwidth = data.KernelSize.Width;
            int kheight = data.KernelSize.Height;

            if (kwidth % 2 == 0)
                kwidth++;
            if (kheight % 2 == 0)
                kheight++;

            int halfWidth = kwidth / 2;
            int halfHeight = kheight / 2;
            rect = rect.Inflate(new Thickness(halfWidth, halfHeight));
        }
        return rect;
    }

    private static void Apply((PixelSize KernelSize, Size Sigma, bool FixImageSize) data, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            var renderTarget = target.RenderTarget!;

            int kWidth = data.KernelSize.Width;
            int kHeight = data.KernelSize.Height;
            if (kWidth <= 0 || kHeight <= 0)
                return;

            if (kWidth % 2 == 0)
                kWidth++;
            if (kHeight % 2 == 0)
                kHeight++;

            Bitmap<Bgra8888>? dst = null;

            try
            {
                using (var src = renderTarget.Snapshot())
                {
                    if (data.FixImageSize)
                    {
                        dst = src.Clone();
                    }
                    else
                    {
                        dst = src.MakeBorder(src.Width + kWidth, src.Height + kHeight);
                    }
                }

                using var mat = dst.ToMat();
                Cv2.GaussianBlur(mat, mat, new(kWidth, kHeight), data.Sigma.Width, data.Sigma.Height);

                EffectTarget newTarget = context.CreateTarget(TransformBounds(data, target.Bounds));
                using (var canvas = context.Open(newTarget))
                {
                    canvas.Clear();
                    canvas.DrawBitmap(dst, Brushes.Resource.White, null);
                }
                target.Dispose();
                context.Targets[i] = newTarget;
            }
            finally
            {
                dst?.Dispose();
            }
        }
    }
}
