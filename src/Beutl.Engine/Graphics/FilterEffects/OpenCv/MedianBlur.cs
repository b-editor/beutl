using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

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
            if (target.RenderTarget is null) continue;

            int kSize = data.KernelSize;
            if (kSize % 2 == 0)
                kSize++;

            Bitmap? src = null;
            Bitmap? dst = null;

            try
            {
                using (var snapshot = target.RenderTarget.Snapshot())
                {
                    if (data.FixImageSize)
                    {
                        src = snapshot.Clone();
                    }
                    else
                    {
                        src = snapshot.MakeBorder(snapshot.Width + kSize, snapshot.Height + kSize);
                    }
                }

                if (src.ColorType != BitmapColorType.Bgra8888)
                {
                    var converted = src.Convert(BitmapColorType.Bgra8888);
                    src.Dispose();
                    src = converted;
                }

                dst = new Bitmap(src.Width, src.Height);
                ApplyMedianFilter(src, dst, kSize);

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
                src?.Dispose();
                dst?.Dispose();
            }
        }
    }

    private static unsafe void ApplyMedianFilter(Bitmap src, Bitmap dst, int kSize)
    {
        int width = src.Width;
        int height = src.Height;
        int half = kSize / 2;
        int medianIndex = (kSize * kSize) / 2;

        nint srcAddr = src.Data;
        nint dstAddr = dst.Data;

        Parallel.For(0, height, y =>
        {
            Span<byte> window = stackalloc byte[kSize * kSize];

            for (int x = 0; x < width; x++)
            {
                // Process each channel (B, G, R, A)
                for (int ch = 0; ch < 4; ch++)
                {
                    int count = 0;
                    for (int ky = -half; ky <= half; ky++)
                    {
                        int sy = Math.Clamp(y + ky, 0, height - 1);
                        for (int kx = -half; kx <= half; kx++)
                        {
                            int sx = Math.Clamp(x + kx, 0, width - 1);
                            window[count++] = ((byte*)srcAddr)[(sy * width + sx) * 4 + ch];
                        }
                    }

                    window[..count].Sort();
                    ((byte*)dstAddr)[(y * width + x) * 4 + ch] = window[count / 2];
                }
            }
        });
    }
}
