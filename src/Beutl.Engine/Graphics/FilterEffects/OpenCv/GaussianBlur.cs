using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Serialization;
using Beutl.Serialization.Migration;

using OpenCvSharp;

using SkiaSharp;

namespace Beutl.Graphics.Effects.OpenCv;

public class GaussianBlur : FilterEffect
{
    public static readonly CoreProperty<PixelSize> KernelSizeProperty;
    public static readonly CoreProperty<Size> SigmaProperty;
    public static readonly CoreProperty<bool> FixImageSizeProperty;
    private PixelSize _kernelSize;
    private Size _sigma;
    private bool _fixImageSize;

    static GaussianBlur()
    {
        KernelSizeProperty = ConfigureProperty<PixelSize, GaussianBlur>(nameof(KernelSize))
            .Accessor(o => o.KernelSize, (o, v) => o.KernelSize = v)
            .DefaultValue(PixelSize.Empty)
            .Register();

        SigmaProperty = ConfigureProperty<Size, GaussianBlur>(nameof(Sigma))
            .Accessor(o => o.Sigma, (o, v) => o.Sigma = v)
            .DefaultValue(Size.Empty)
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
    public Size Sigma
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
        context.CustomEffect((KernelSize, Sigma, FixImageSize), Apply, TransformBounds);
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
            var surface = target.Surface!;

            int kwidth = data.KernelSize.Width;
            int kheight = data.KernelSize.Height;
            if (kwidth <= 0 || kheight <= 0)
                return;

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
                Cv2.GaussianBlur(mat, mat, new(kwidth, kheight), data.Sigma.Width, data.Sigma.Height);

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
        return TransformBounds((KernelSize, Sigma, FixImageSize), rect);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        // Todo: 互換性処理
        if (context is IJsonSerializationContext jsonContext)
        {
            JsonObject json = jsonContext.GetJsonObject();

            try
            {
                JsonNode? animations = json["Animations"] ?? json["animations"];
                JsonNode? sigma = animations?[nameof(Sigma)];

                if (sigma != null)
                {
                    Migration_ChangeSigmaType.Update(sigma);
                }
            }
            catch
            {
            }
        }

        base.Deserialize(context);
    }
}
