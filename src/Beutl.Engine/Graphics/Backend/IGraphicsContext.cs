using SkiaSharp;

namespace Beutl.Graphics.Backend;

internal interface IGraphicsContext : IDisposable
{
    GraphicsBackend Backend { get; }

    GRContext SkiaContext { get; }

    GpuInfo? GpuInfo { get; }

    ISharedTexture CreateTexture(int width, int height, TextureFormat format);

    void WaitIdle();
}
