using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.Graphics.Backend;

/// <summary>The process-wide graphics state <see cref="GraphicsContextFactory.Shutdown"/> tears down.</summary>
internal readonly record struct InstalledGraphics(
    IGraphicsContext? SharedContext,
    VulkanInstance? VulkanInstance,
    VulkanPhysicalDeviceInfo? PhysicalDevice,
    bool FailedToInitialize);
