using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe partial class VulkanContext
{
    /// <summary>
    /// Resolves a Vulkan entry point for Skia, substituting this context's proxies for the image
    /// create/destroy/bind calls it intercepts.
    /// </summary>
    internal IntPtr GetVulkanProcAddress(string name, IntPtr instance, IntPtr device)
    {
        if (name == "vkCreateImage")
            return _createImageProxy;
        if (name == "vkDestroyImage")
            return _destroyImageProxy;
        if (name == "vkBindImageMemory")
            return _bindImageMemoryProxy;
        if (name == "vkBindImageMemory2" && _bindImageMemory2Proxy != IntPtr.Zero)
            return _bindImageMemory2Proxy;
        if (name == "vkBindImageMemory2KHR" && _bindImageMemory2KhrProxy != IntPtr.Zero)
            return _bindImageMemory2KhrProxy;

        var vk = _vulkanInstance.Vk;

        if (device != IntPtr.Zero)
        {
            var deviceHandle = new Device(device);
            var addr = vk.GetDeviceProcAddr(deviceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        if (instance != IntPtr.Zero)
        {
            var instanceHandle = new Instance(instance);
            var addr = vk.GetInstanceProcAddr(instanceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        return vk.GetInstanceProcAddr(_vulkanInstance.Instance, name);
    }

    private T GetDeviceDelegate<T>(string name)
        where T : Delegate
        => TryGetDeviceDelegate<T>(name)
           ?? throw new InvalidOperationException($"Vulkan device function '{name}' is unavailable.");

    private T? TryGetDeviceDelegate<T>(string name)
        where T : Delegate
    {
        IntPtr address = _vulkanInstance.Vk.GetDeviceProcAddr(_vulkanDevice.Device, name);
        return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private unsafe Result CreateSkiaImage(
        Device device,
        ImageCreateInfo* createInfo,
        AllocationCallbacks* allocator,
        Image* image)
    {
        ImageCreateInfo initializedInfo = PrepareSkiaImageCreateInfo(*createInfo);

        Result result = _createImage(device, &initializedInfo, allocator, image);
        if (result == Result.Success)
        {
            lock (_skiaImagesLock)
                _skiaImages[image->Handle] = initializedInfo;
        }
        return result;
    }

    private unsafe void DestroySkiaImage(
        Device device,
        Image image,
        AllocationCallbacks* allocator)
    {
        lock (_skiaImagesLock)
            _skiaImages.Remove(image.Handle);
        _destroyImage(device, image, allocator);
    }

    private unsafe Result BindSkiaImageMemory(
        Device device,
        Image image,
        DeviceMemory memory,
        ulong memoryOffset)
    {
        Result result = _bindImageMemory(device, image, memory, memoryOffset);
        ImageCreateInfo createInfo;
        lock (_skiaImagesLock)
            _skiaImages.TryGetValue(image.Handle, out createInfo);
        if (result == Result.Success && RequiresTransparentInitialization(createInfo))
        {
            try
            {
                ClearSkiaImage(image, createInfo);
            }
            catch (Exception ex)
            {
                // Never let a managed exception cross the unmanaged Vulkan callback boundary.
                // Rejecting the bind makes Skia discard the allocation instead of observing
                // an image whose contents were never defined.
                s_logger.LogError(ex, "Failed to initialize a Skia Vulkan image.");
                return Result.ErrorInitializationFailed;
            }
        }
        return result;
    }

    private unsafe Result BindSkiaImageMemory2(
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos)
        => BindSkiaImageMemoryBatch(_bindImageMemory2!, device, bindInfoCount, bindInfos);

    private unsafe Result BindSkiaImageMemory2Khr(
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos)
        => BindSkiaImageMemoryBatch(_bindImageMemory2Khr!, device, bindInfoCount, bindInfos);

    // vkBindImageMemory2 binds the whole batch or none of it, so initialization follows a successful call
    // and covers every image in the batch that the single-bind path would have cleared.
    private unsafe Result BindSkiaImageMemoryBatch(
        VkBindImageMemory2Delegate bind,
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos)
    {
        Result result = bind(device, bindInfoCount, bindInfos);
        if (result != Result.Success || bindInfos is null)
            return result;

        for (uint index = 0; index < bindInfoCount; index++)
        {
            Image image = bindInfos[index].Image;
            ImageCreateInfo createInfo;
            lock (_skiaImagesLock)
                _skiaImages.TryGetValue(image.Handle, out createInfo);
            if (!RequiresTransparentInitialization(createInfo))
                continue;

            try
            {
                ClearSkiaImage(image, createInfo);
            }
            catch (Exception ex)
            {
                // Never let a managed exception cross the unmanaged Vulkan callback boundary. The bind
                // already succeeded here, so the batch cannot be undone; reporting the failure makes Skia
                // discard the allocation rather than draw from memory whose contents were never defined.
                s_logger.LogError(ex, "Failed to initialize a Skia Vulkan image.");
                return Result.ErrorInitializationFailed;
            }
        }

        return result;
    }

    internal static ImageCreateInfo PrepareSkiaImageCreateInfo(ImageCreateInfo createInfo)
    {
        if ((createInfo.Usage & ImageUsageFlags.ColorAttachmentBit) != 0)
            createInfo.Usage |= ImageUsageFlags.TransferDstBit;
        return createInfo;
    }

    internal static bool RequiresTransparentInitialization(ImageCreateInfo createInfo)
        => createInfo.InitialLayout == ImageLayout.Undefined
           && (createInfo.Usage & ImageUsageFlags.ColorAttachmentBit) != 0;

    internal static ImageSubresourceRange CreateInitializationRange(ImageCreateInfo createInfo)
        => new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = createInfo.MipLevels,
            BaseArrayLayer = 0,
            LayerCount = createInfo.ArrayLayers,
        };

    // This leaves the image in TransferDstOptimal while Ganesh still holds Undefined for it: Skia hands
    // out no backend handle for an image it allocated itself, so the mutable-state interop used for our
    // shared textures cannot synchronize these internal allocations.
    // A first use transitioning out of Undefined may therefore discard the clear, so what this guarantees is
    // a zeroed backing allocation - no recycled allocation's bytes - not defined pixels at the Vulkan level.
    private unsafe void ClearSkiaImage(Image image, ImageCreateInfo createInfo)
    {
        _vulkanCommandPool.SubmitIsolatedCommands(commandBuffer =>
        {
            ImageSubresourceRange range = CreateInitializationRange(createInfo);
            var barrier = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = range,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit,
            };
            Vk.CmdPipelineBarrier(
                commandBuffer,
                PipelineStageFlags.TopOfPipeBit,
                PipelineStageFlags.TransferBit,
                0,
                0, null,
                0, null,
                1, &barrier);

            var transparent = new ClearColorValue(0, 0, 0, 0);
            Vk.CmdClearColorImage(
                commandBuffer,
                image,
                ImageLayout.TransferDstOptimal,
                &transparent,
                1,
                &range);
        });
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate Result VkCreateImageDelegate(
        Device device,
        ImageCreateInfo* createInfo,
        AllocationCallbacks* allocator,
        Image* image);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate void VkDestroyImageDelegate(
        Device device,
        Image image,
        AllocationCallbacks* allocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate Result VkBindImageMemoryDelegate(
        Device device,
        Image image,
        DeviceMemory memory,
        ulong memoryOffset);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private unsafe delegate Result VkBindImageMemory2Delegate(
        Device device,
        uint bindInfoCount,
        BindImageMemoryInfo* bindInfos);
}
