using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>A CPU target over the linear-sRGB, premultiplied RgbaF16 surface that renderers request.</summary>
internal sealed class CpuRenderTarget(PixelSize size)
    : RenderTarget(
        SKSurface.Create(new SKImageInfo(
                size.Width,
                size.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear()))
            ?? throw new InvalidOperationException("Could not create a CPU test surface."),
        size.Width,
        size.Height)
{
    public CpuRenderTarget(int width, int height)
        : this(new PixelSize(width, height))
    {
    }
}

/// <summary>Allocates every target a renderer requests as a <see cref="CpuRenderTarget"/>.</summary>
internal sealed class CpuRenderTargetFactory : IRenderTargetFactory
{
    public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        => new CpuRenderTarget(allocation.DeviceSize);
}
