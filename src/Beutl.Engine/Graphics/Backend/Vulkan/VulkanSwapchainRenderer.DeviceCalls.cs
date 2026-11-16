using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed unsafe partial class VulkanSwapchainRenderer
{
    // The calls on the renderer's own device, surface and swapchain.
    private sealed class DeviceCalls(VulkanSwapchainRenderer owner) : IVulkanPresentCalls
    {
        public Extent2D SwapchainExtent => owner._swapchain!.Extent;

        public bool HasSourceImage => owner._sourceImage.Handle != 0;

        public Extent2D? GetSurfaceExtent() => owner._swapchain!.GetSurfaceExtent();

        public Result WaitForFences(ulong timeoutNanoseconds)
        {
            var fence = owner._inFlightFence;
            return owner._vk.WaitForFences(owner._device, 1, &fence, Vk.True, timeoutNanoseconds);
        }

        public Result ResetFences()
        {
            var fence = owner._inFlightFence;
            return owner._vk.ResetFences(owner._device, 1, &fence);
        }

        public Result DeviceWaitIdle() => owner._vk.DeviceWaitIdle(owner._device);

        public void RecreateSwapchain(uint width, uint height)
        {
            owner._swapchain!.Recreate(width, height);
            owner._pipeline!.RecreateFramebuffers(owner._swapchain.ImageViews, owner._swapchain.Extent);
        }

        public void UploadBitmap(Bitmap bitmap) => owner.UploadBitmap(bitmap);

        public void UploadOverlay() => owner.UploadOverlay();

        public Result AcquireNextImage(out uint imageIndex)
            => owner._swapchain!.AcquireNextImage(owner._imageAvailableSemaphore, out imageIndex);

        public void RecordCommands(uint imageIndex, RenderParams renderParams)
            => owner.RecordRenderCommands(imageIndex, renderParams);

        public Result QueueSubmit() => owner.SubmitRenderCommands();

        public Result QueuePresent(uint imageIndex)
            => owner._swapchain!.Present(owner._graphicsQueue, owner._renderFinishedSemaphore, imageIndex);
    }
}
