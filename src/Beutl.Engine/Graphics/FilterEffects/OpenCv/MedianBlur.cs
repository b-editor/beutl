using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;
using OpenCvSharp;

namespace Beutl.Graphics.Effects.OpenCv;

[Display(Name = "CvMedianBlur")]
public partial class MedianBlur : FilterEffect
{
    public MedianBlur()
    {
        ScanProperties<MedianBlur>();
    }

    [Display(Name = nameof(GraphicsStrings.KernelSize), ResourceType = typeof(GraphicsStrings))]
    [Range(0, int.MaxValue)]
    public IProperty<int> KernelSize { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(GraphicsStrings.FixImageSize), ResourceType = typeof(GraphicsStrings))]
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

            Bitmap? dst = null;

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

                // OpenCVはBgra8888 (CV_8UC4) を前提とするため、必要に応じて変換
                if (dst.ColorType != BitmapColorType.Bgra8888)
                {
                    var converted = dst.Convert(BitmapColorType.Bgra8888);
                    dst.Dispose();
                    dst = converted;
                }

                using var mat = dst.ToMat();
                Cv2.MedianBlur(mat, mat, kSize);

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
