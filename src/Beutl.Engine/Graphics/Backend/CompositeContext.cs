using System.Runtime.InteropServices;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

internal sealed class CompositeContext : IGraphicsContext
{
    private bool _disposed;

    public CompositeContext(bool enableVulkanValidation = false)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("DualContext is only available on macOS");
        }

        Metal = new MetalContext();
        Vulkan = new VulkanContext(enableVulkanValidation);
    }

    public GraphicsBackend Backend => GraphicsBackend.Metal;

    public GRContext SkiaContext => Metal.SkiaContext;

    public MetalContext Metal { get; }

    public VulkanContext Vulkan { get; }

    public ISharedTexture CreateTexture(int width, int height, TextureFormat format)
    {
        return new MetalVulkanSharedTexture(Metal, Vulkan, width, height, format);
    }

    public void WaitIdle()
    {
        Vulkan?.WaitIdle();
        Metal.WaitIdle();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Vulkan?.Dispose();
        Metal.Dispose();
    }
}
