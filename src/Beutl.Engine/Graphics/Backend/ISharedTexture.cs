using SkiaSharp;

namespace Beutl.Graphics.Backend;

internal interface ISharedTexture : IDisposable
{
    int Width { get; }

    int Height { get; }

    TextureFormat Format { get; }

    IntPtr VulkanImageHandle { get; }

    SKSurface CreateSkiaSurface();
}
