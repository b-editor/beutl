using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using OpenCvSharp;

namespace Beutl.Graphics.Effects.OpenCv;

public partial class Blur : FilterEffect
{
    public Blur()
    {
        ScanProperties<Blur>();
    }

    [Display(Name = nameof(Strings.KernelSize), ResourceType = typeof(Strings))]
    [Range(typeof(PixelSize), "0,0", "max,max")]
    public IProperty<PixelSize> KernelSize { get; } = Property.CreateAnimatable(PixelSize.Empty);

    [Display(Name = nameof(Strings.FixImageSize), ResourceType = typeof(Strings))]
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
                Cv2.Blur(mat, mat, new(kWidth, kHeight));

                EffectTarget newTarget = context.CreateTarget(TransformBounds(data, target.Bounds));
                using (var canvas = context.Open(newTarget))
                {
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
