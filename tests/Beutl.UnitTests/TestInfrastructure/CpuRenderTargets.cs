using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.TestInfrastructure;

/// <summary>
/// CPU render targets for tests. <see cref="RenderTarget.Create(int, int)"/> makes the same surface off the render
/// thread, but a GPU texture on it.
/// </summary>
internal static class CpuRenderTargets
{
    public static RenderTarget Create(int width, int height) => new CpuRenderTarget(width, height);

    /// <summary>An RgbaF16, premultiplied, linear sRGB raster surface.</summary>
    public static SKSurface CreateSurface(int width, int height)
        => SKSurface.Create(new SKImageInfo(
               width,
               height,
               SKColorType.RgbaF16,
               SKAlphaType.Premul,
               SKColorSpace.CreateSrgbLinear()))
           ?? throw new InvalidOperationException("A CPU test surface could not be created.");

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height);
}

/// <summary>A render-target factory that declines every allocation.</summary>
internal sealed class DecliningRenderTargetFactory : IRenderTargetFactory
{
    public RenderTarget? Create(RenderTargetAllocationDescriptor allocation) => null;
}
