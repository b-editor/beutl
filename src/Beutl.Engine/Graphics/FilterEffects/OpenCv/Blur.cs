using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using OpenCvSharp;

namespace Beutl.Graphics.Effects.OpenCv;

public partial class Blur : FilterEffect<Blur.Node, Blur.Options>
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

    public override Node CreateNode(Options options)
    {
        return new Node(options);
    }

    public override Options CreateOptions(RenderContext context)
    {
        return new Options(context.Get(KernelSize), context.Get(FixImageSize));
    }

    public record struct Options(PixelSize KernelSize, bool FixImageSize);

    public class Node(Options options) : FilterEffectRenderNode<Options>(options)
    {
        public override void ApplyTo(FilterEffectContext context)
        {
            context.CustomEffect((Options.KernelSize, Options.FixImageSize), Apply, TransformBounds);
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
}
