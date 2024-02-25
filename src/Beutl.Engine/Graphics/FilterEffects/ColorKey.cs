using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Rendering;

using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class ColorKey : FilterEffect
{
    public static readonly CoreProperty<Color> ColorProperty;
    public static readonly CoreProperty<float> RangeProperty;
    private Color _color;
    private float _range;

    static ColorKey()
    {
        ColorProperty = ConfigureProperty<Color, ColorKey>(nameof(Color))
            .Accessor(o => o.Color, (o, v) => o.Color = v)
            .Register();

        RangeProperty = ConfigureProperty<float, ColorKey>(nameof(Range))
            .Accessor(o => o.Range, (o, v) => o.Range = v)
            .Register();

        AffectsRender<ColorKey>(ColorProperty, RangeProperty);
    }

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public Color Color
    {
        get => _color;
        set => SetAndRaise(ColorProperty, ref _color, value);
    }

    [Display(Name = nameof(Strings.BrightnessRange), ResourceType = typeof(Strings))]
    public float Range
    {
        get => _range;
        set => SetAndRaise(RangeProperty, ref _range, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        var colorNtsc =
            (_color.R * 0.11448f) +
            (_color.G * 0.58661f) +
            (_color.B * 0.29891f);
        colorNtsc = Math.Clamp(colorNtsc, 0, 255);
        colorNtsc = MathF.Round(colorNtsc);

        context.CustomEffect((colorNtsc, Range), OnApplyTo, (_, r) => r);
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

    private unsafe void OnApplyTo((float colorNtsc, float range) data, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            var surface = target.Surface!.Value;
            Accelerator accelerator = SharedGPUContext.Accelerator;
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<Bgra8888>, float, float>(EffectKernel);

            var size = PixelSize.FromSize(target.Bounds.Size, 1);
            var imgInfo = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888);

            using var source = accelerator.Allocate1D<Bgra8888>(size.Width * size.Height);

            CopyFromCPU(source, surface, imgInfo);

            kernel((int)source.Length, source.View, data.colorNtsc, data.range);

            SKCanvas canvas = surface.Canvas;
            canvas.Clear();

            using var skBmp = new SKBitmap(imgInfo);

            CopyToCPU(source, skBmp);

            canvas.DrawBitmap(skBmp, 0, 0);
        }
    }

    private static void EffectKernel(Index1D index, ArrayView<Bgra8888> src, float colorNtsc, float range)
    {
        Bgra8888 pixel = src[index];
        float ntsc =
            (pixel.R * 0.11448f) +
            (pixel.G * 0.58661f) +
            (pixel.B * 0.29891f);

        ntsc = IntrinsicMath.Clamp(ntsc, 0, 255);
        ntsc = XMath.Round(ntsc);

        if (IntrinsicMath.Abs(colorNtsc - ntsc) < range)
        {
            src[index] = default;
        }
    }
}
