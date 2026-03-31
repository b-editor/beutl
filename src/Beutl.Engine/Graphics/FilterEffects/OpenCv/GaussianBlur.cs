using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects.OpenCv;

[Display(Name = "CvGaussianBlur")]
public partial class GaussianBlur : FilterEffect
{
    public GaussianBlur()
    {
        ScanProperties<GaussianBlur>();
    }

    [Display(Name = nameof(GraphicsStrings.KernelSize), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(PixelSize), "0,0", "max,max")]
    public IProperty<PixelSize> KernelSize { get; } = Property.CreateAnimatable(PixelSize.Empty);

    [Display(Name = nameof(GraphicsStrings.Sigma), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    [Display(Name = nameof(GraphicsStrings.FixImageSize), ResourceType = typeof(GraphicsStrings))]
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

    // OpenCV formula: when sigma is 0, compute from kernel size
    private static float ComputeSigma(float sigma, int ksize)
    {
        if (sigma > 0)
            return sigma;
        return (float)(0.3 * ((ksize - 1) * 0.5 - 1) + 0.8);
    }

    private static void Apply((PixelSize KernelSize, Size Sigma, bool FixImageSize) data, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            if (target.RenderTarget is null) continue;

            int kWidth = data.KernelSize.Width;
            int kHeight = data.KernelSize.Height;
            if (kWidth <= 0 || kHeight <= 0)
                return;

            if (kWidth % 2 == 0)
                kWidth++;
            if (kHeight % 2 == 0)
                kHeight++;

            float sigmaX = ComputeSigma(data.Sigma.Width, kWidth);
            float sigmaY = ComputeSigma(data.Sigma.Height, kHeight);

            EffectTarget newTarget = context.CreateTarget(TransformBounds(data, target.Bounds));
            using (var canvas = context.Open(newTarget))
            {
                canvas.Clear();
                using var filter = SKImageFilter.CreateBlur(sigmaX, sigmaY);
                using var paint = new SKPaint { ImageFilter = filter };
                using (canvas.PushPaint(paint))
                {
                    canvas.DrawRenderTarget(target.RenderTarget, default);
                }
            }
            target.Dispose();
            context.Targets[i] = newTarget;
        }
    }
}
