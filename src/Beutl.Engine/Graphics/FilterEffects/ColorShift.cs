using Beutl.Graphics.Rendering;
using Beutl.Media;
using ILGPU;
using ILGPU.Runtime;
using OpenCvSharp;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class ColorShift : FilterEffect
{
    public static readonly CoreProperty<PixelPoint> RedOffsetProperty;
    public static readonly CoreProperty<PixelPoint> GreenOffsetProperty;
    public static readonly CoreProperty<PixelPoint> BlueOffsetProperty;
    public static readonly CoreProperty<PixelPoint> AlphaOffsetProperty;
    private PixelPoint _redOffset;
    private PixelPoint _greenOffset;
    private PixelPoint _blueOffset;
    private PixelPoint _alphaOffset;

    static ColorShift()
    {
        RedOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(RedOffset))
            .Accessor(o => o.RedOffset, (o, v) => o.RedOffset = v)
            .Register();

        GreenOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(GreenOffset))
            .Accessor(o => o.GreenOffset, (o, v) => o.GreenOffset = v)
            .Register();

        BlueOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(BlueOffset))
            .Accessor(o => o.BlueOffset, (o, v) => o.BlueOffset = v)
            .Register();

        AlphaOffsetProperty = ConfigureProperty<PixelPoint, ColorShift>(nameof(AlphaOffset))
            .Accessor(o => o.AlphaOffset, (o, v) => o.AlphaOffset = v)
            .Register();

        AffectsRender<ColorShift>(
            RedOffsetProperty, GreenOffsetProperty, BlueOffsetProperty, AlphaOffsetProperty);
    }

    public PixelPoint RedOffset
    {
        get => _redOffset;
        set => SetAndRaise(RedOffsetProperty, ref _redOffset, value);
    }

    public PixelPoint GreenOffset
    {
        get => _greenOffset;
        set => SetAndRaise(GreenOffsetProperty, ref _greenOffset, value);
    }

    public PixelPoint BlueOffset
    {
        get => _blueOffset;
        set => SetAndRaise(BlueOffsetProperty, ref _blueOffset, value);
    }

    public PixelPoint AlphaOffset
    {
        get => _alphaOffset;
        set => SetAndRaise(AlphaOffsetProperty, ref _alphaOffset, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((RedOffset, GreenOffset, BlueOffset, AlphaOffset), OnApply, TransformBoundsCore);
    }

    private static Rect TransformBoundsCore(
        (PixelPoint RedOffset, PixelPoint GreenOffset, PixelPoint BlueOffset, PixelPoint AlphaOffset) d,
        Rect bounds)
    {
        return bounds.Translate(d.RedOffset.ToPoint(1))
            .Union(bounds.Translate(d.GreenOffset.ToPoint(1)))
            .Union(bounds.Translate(d.BlueOffset.ToPoint(1)))
            .Union(bounds.Translate(d.AlphaOffset.ToPoint(1)));
    }

    private static void OnApply(
        (PixelPoint RedOffset, PixelPoint GreenOffset, PixelPoint BlueOffset, PixelPoint AlphaOffset) data,
        CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            var target = context.Targets[i];
            var surface = target.Surface!;

            var bounds = TransformBoundsCore(data, target.Bounds);
            var pixelRect = PixelRect.FromRect(bounds);
            int minOffsetX = Math.Min(data.RedOffset.X,
                Math.Min(data.GreenOffset.X, Math.Min(data.BlueOffset.X, data.AlphaOffset.X)));
            int minOffsetY = Math.Min(data.RedOffset.Y,
                Math.Min(data.GreenOffset.Y, Math.Min(data.BlueOffset.Y, data.AlphaOffset.Y)));

            var size = surface.Value.Canvas.DeviceClipBounds.Size;
            Accelerator accelerator = SharedGPUContext.Accelerator;
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView2D<Vec4b, Stride2D.DenseX>, ArrayView2D<Vec4b, Stride2D.DenseX>,
                PixelPoint, PixelPoint, PixelPoint, PixelPoint, PixelPoint>(
                Kernel);
            using var source = accelerator.Allocate2DDenseX<Vec4b>(new(size.Width, size.Height));
            using var dest = accelerator.Allocate2DDenseX<Vec4b>(new(pixelRect.Width, pixelRect.Height));

            SharedGPUContext.CopyFromCPU(source, surface.Value,
                new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888));

            kernel(
                new Index2D(size.Width, size.Height),
                source, dest,
                data.RedOffset, data.GreenOffset, data.BlueOffset, data.AlphaOffset,
                new PixelPoint(minOffsetX, minOffsetY));

            using var skBmp =
                new SKBitmap(new SKImageInfo(pixelRect.Width, pixelRect.Height, SKColorType.Bgra8888));

            SharedGPUContext.CopyToCPU(dest, skBmp);

            EffectTarget newTarget = context.CreateTarget(bounds);
            newTarget.Surface!.Value.Canvas.DrawBitmap(skBmp, 0,0);

            target.Dispose();
            context.Targets[i] = newTarget;
        }
    }

    private static void Kernel(
        Index2D index, ArrayView2D<Vec4b, Stride2D.DenseX> src, ArrayView2D<Vec4b, Stride2D.DenseX> dst,
        PixelPoint redOffset, PixelPoint greenOffset, PixelPoint blueOffset, PixelPoint alphaOffset,
        PixelPoint minOffset)
    {
        var color = src[index.X, index.Y];

        dst[index.X + redOffset.X - minOffset.X, index.Y + redOffset.Y - minOffset.Y].Item2 = color.Item2;
        dst[index.X + greenOffset.X - minOffset.X, index.Y + greenOffset.Y - minOffset.Y].Item1 =
            color.Item1;
        dst[index.X + blueOffset.X - minOffset.X, index.Y + blueOffset.Y - minOffset.Y].Item0 =
            color.Item0;
        dst[index.X + alphaOffset.X - minOffset.X, index.Y + alphaOffset.Y - minOffset.Y].Item3 =
            color.Item3;
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return TransformBoundsCore((RedOffset, GreenOffset, BlueOffset, AlphaOffset), bounds);
    }
}
