using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

internal sealed class CompositeContext : IGraphicsContext
{
    private bool _disposed;

    public CompositeContext(VulkanInstance vulkanInstance, VulkanPhysicalDeviceInfo physicalDevice)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("CompositeContext is only available on macOS");
        }

        // MoltenVKの場合はMetalコンテキストを使用する
        if (physicalDevice.IsMoltenVK)
        {
            Metal = new MetalContext();
        }

        Vulkan = new VulkanContext(vulkanInstance, physicalDevice);
    }

    public GraphicsBackend Backend => GraphicsBackend.Metal;

    public GRContext SkiaContext => Metal?.SkiaContext ?? Vulkan.SkiaContext!;

    public MetalContext? Metal { get; }

    public VulkanContext Vulkan { get; }

    public ISharedTexture CreateTexture(int width, int height, TextureFormat format)
    {
        if (Metal == null)
            return Vulkan.CreateTexture(width, height, format);

        return new MetalVulkanSharedTexture(Metal, Vulkan, width, height, format);
    }

    public void WaitIdle()
    {
        Vulkan.WaitIdle();
        Metal?.WaitIdle();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Vulkan.Dispose();
        Metal?.Dispose();
    }
}
