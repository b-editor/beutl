using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// The Vulkan work <see cref="VulkanSwapchainRenderer"/>'s presentation thread does. The renderer decides what to do
/// with each result; tests stand in for the device to return results a real one gives only by failing.
/// </summary>
internal interface IVulkanPresentCalls
{
    /// <summary>The swapchain's size.</summary>
    Extent2D SwapchainExtent { get; }

    /// <summary>Whether a frame has been uploaded, which drawing it again needs.</summary>
    bool HasSourceImage { get; }

    /// <summary>The surface's size (vkGetPhysicalDeviceSurfaceCapabilitiesKHR), or null when it has no usable one.</summary>
    Extent2D? GetSurfaceExtent();

    /// <summary>vkWaitForFences on the fence of the frame in flight.</summary>
    Result WaitForFences(ulong timeoutNanoseconds);

    /// <summary>vkResetFences on the fence of the frame in flight.</summary>
    Result ResetFences();

    /// <summary>vkDeviceWaitIdle.</summary>
    Result DeviceWaitIdle();

    /// <summary>Recreates the swapchain at the size, and the framebuffers over its images.</summary>
    void RecreateSwapchain(uint width, uint height);

    /// <summary>Copies the frame into the source image and submits the copy.</summary>
    void UploadBitmap(Bitmap bitmap);

    /// <summary>Draws the UI's overlay at the swapchain's size and submits its upload.</summary>
    void UploadOverlay();

    /// <summary>vkAcquireNextImageKHR.</summary>
    Result AcquireNextImage(out uint imageIndex);

    /// <summary>Records the draw of the source image into the swapchain image.</summary>
    void RecordCommands(uint imageIndex, RenderParams renderParams);

    /// <summary>vkQueueSubmit of the recorded draw, which signals the fence of the frame in flight.</summary>
    Result QueueSubmit();

    /// <summary>vkQueuePresentKHR.</summary>
    Result QueuePresent(uint imageIndex);
}
