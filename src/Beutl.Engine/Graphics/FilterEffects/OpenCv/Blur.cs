using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Effects.OpenCv;

[Display(Name = "CvBlur")]
public partial class Blur : FilterEffect
{
    public Blur()
    {
        ScanProperties<Blur>();
    }

    [Display(Name = nameof(GraphicsStrings.KernelSize), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(PixelSize), "0,0", "max,max")]
    public IProperty<PixelSize> KernelSize { get; } = Property.CreateAnimatable(PixelSize.Empty);

    [Display(Name = nameof(GraphicsStrings.FixImageSize), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> FixImageSize { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect((r.KernelSize, r.FixImageSize), Apply, TransformBounds);
    }

    private static Rect TransformBounds((PixelSize KernelSize, bool FixImageSize) data, Rect rect)
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

    private static void Apply((PixelSize KernelSize, bool FixImageSize) data, CustomFilterEffectContext context)
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

            // Box blur approximated via Gaussian: sigma = sqrt((K^2 - 1) / 12)
            float sigmaX = MathF.Sqrt((kWidth * kWidth - 1) / 12f);
            float sigmaY = MathF.Sqrt((kHeight * kHeight - 1) / 12f);

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
