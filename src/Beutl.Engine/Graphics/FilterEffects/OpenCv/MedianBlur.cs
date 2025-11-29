using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using OpenCvSharp;

namespace Beutl.Graphics.Effects.OpenCv;

public partial class MedianBlur : FilterEffect
{
    public MedianBlur()
    {
        ScanProperties<MedianBlur>();
    }

    [Display(Name = nameof(Strings.KernelSize), ResourceType = typeof(Strings))]
    [Range(0, int.MaxValue)]
    public IProperty<int> KernelSize { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(Strings.FixImageSize), ResourceType = typeof(Strings))]
    public IProperty<bool> FixImageSize { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect((r.KernelSize, r.FixImageSize), Apply, TransformBounds);
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
            var renderTarget = target.RenderTarget!;
            int kSize = data.KernelSize;
            if (kSize % 2 == 0)
                kSize++;

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
                        dst = src.MakeBorder(src.Width + kSize, src.Height + kSize);
                    }
                }

                using var mat = dst.ToMat();
                Cv2.MedianBlur(mat, mat, kSize);

                EffectTarget newTarget = context.CreateTarget(TransformBounds(data, target.Bounds));
                newTarget.RenderTarget!.Value.Canvas.DrawBitmap(dst.ToSKBitmap(), 0, 0);
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
