using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Rendering;

using ILGPU;
using ILGPU.Runtime;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class ChromaKey : FilterEffect
{
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<float> HueRangeProperty;
    public static readonly CoreProperty<float> SaturationRangeProperty;
    private Color _color;
    private float _hueRange;
    private float _saturationRange;

    static ChromaKey()
    {
        ColorProperty = ConfigureProperty<Color, ChromaKey>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .Register();

        HueRangeProperty = ConfigureProperty<float, ChromaKey>(nameof(HueRange))
            .Accessor(o => o.HueRange, (o, v) => o.HueRange = v)
            .Register();

        SaturationRangeProperty = ConfigureProperty<float, ChromaKey>(nameof(SaturationRange))
            .Accessor(o => o.SaturationRange, (o, v) => o.SaturationRange = v)
            .Register();

        AffectsRender<ChromaKey>(ColorProperty, HueRangeProperty, SaturationRangeProperty);
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    [Display(Name = nameof(Strings.HueRange), ResourceType = typeof(Strings))]
    public float HueRange
    {
        get => _hueRange;
        set => SetAndRaise(HueRangeProperty, ref _hueRange, value);
    }

    [Display(Name = nameof(Strings.SaturationRange), ResourceType = typeof(Strings))]
    public float SaturationRange
    {
        get => _saturationRange;
        set => SetAndRaise(SaturationRangeProperty, ref _saturationRange, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((Color.ToHsv(), HueRange, SaturationRange), OnApplyTo, (_, r) => r);
    }

    private static unsafe void CopyFromCPU(MemoryBuffer1D<Bgra8888, Stride1D.Dense> source, SKSurface surface, SKImageInfo imageInfo)
    {
        void* tmp = NativeMemory.Alloc((nuint)source.LengthInBytes);
        try
        {
            bool result = surface.ReadPixels(imageInfo, (nint)tmp, imageInfo.Width * 4, 0, 0);

            source.View.CopyFromCPU(ref Unsafe.AsRef<Bgra8888>(tmp), source.Length);
        }
        finally
        {
            NativeMemory.Free(tmp);
        }
    }

    private static unsafe void CopyToCPU(MemoryBuffer1D<Bgra8888, Stride1D.Dense> source, SKBitmap bitmap)
    {
        source.View.CopyToCPU(ref Unsafe.AsRef<Bgra8888>((void*)bitmap.GetPixels()), source.Length);
    }

    private unsafe void OnApplyTo((Hsv hsv, float hueRange, float satRange) data, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            var surface = target.Surface!.Value;
            Accelerator accelerator = SharedGPUContext.Accelerator;
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<Bgra8888>, Hsv, float, float>(EffectKernel);

            var size = PixelSize.FromSize(target.Bounds.Size, 1);
            var imgInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888);

            using var source = accelerator.Allocate1D<Bgra8888>(size.Width * size.Height);

            CopyFromCPU(source, surface, imgInfo);

            kernel((int)source.Length, source.View, data.hsv, data.hueRange, data.satRange);

            SKCanvas canvas = surface.Canvas;
            canvas.Clear();

            using var skBmp = new SKBitmap(imgInfo);

            CopyToCPU(source, skBmp);

            canvas.DrawBitmap(skBmp, 0, 0);
        }
    }

    private static void EffectKernel(Index1D index, ArrayView<Bgra8888> src, Hsv hsv, float hueRange, float satRange)
    {
        Bgra8888 pixel = src[index];
        var srcHsv = pixel.ToColor().ToHsv();

        if (IntrinsicMath.Abs(hsv.H - srcHsv.H) < hueRange
            && IntrinsicMath.Abs(hsv.S - srcHsv.S) < satRange)
        {
            src[index] = default;
        }
    }
}
