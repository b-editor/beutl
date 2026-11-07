using Beutl.Media;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe partial class VulkanSwapchainRenderer
{
    private void CreateDedicatedDevice()
    {
        _queueFamilyIndex = VulkanPhysicalDeviceQueries.FindGraphicsQueueFamily(_vk, _physicalDevice);

        float queuePriority = 1.0f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _queueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        var extensions = new List<string> { "VK_KHR_swapchain" };
        if (OperatingSystem.IsMacOS())
        {
            if (VulkanPhysicalDeviceQueries.GetDeviceExtensionNames(_vk, _physicalDevice)
                .Contains("VK_KHR_portability_subset"))
            {
                extensions.Add("VK_KHR_portability_subset");
            }
        }

        using (var extensionNames = new VulkanNameArray(extensions))
        {
            var features = new PhysicalDeviceFeatures();
            fixed (byte** ppExtensions = extensionNames.Pointers)
            {
                var createInfo = new DeviceCreateInfo
                {
                    SType = StructureType.DeviceCreateInfo,
                    QueueCreateInfoCount = 1,
                    PQueueCreateInfos = &queueCreateInfo,
                    EnabledExtensionCount = extensionNames.Count,
                    PpEnabledExtensionNames = ppExtensions,
                    PEnabledFeatures = &features
                };

                Device device;
                var result = _vk.CreateDevice(_physicalDevice, &createInfo, null, &device);
                if (result != Result.Success)
                    throw new InvalidOperationException($"Failed to create dedicated presentation device: {result}");

                _device = device;
            }
        }

        _vk.GetDeviceQueue(_device, _queueFamilyIndex, 0, out _graphicsQueue);

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _queueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit | CommandPoolCreateFlags.TransientBit
        };

        CommandPool pool;
        var poolResult = _vk.CreateCommandPool(_device, &poolInfo, null, &pool);
        if (poolResult != Result.Success)
            throw new InvalidOperationException($"Failed to create command pool: {poolResult}");

        _commandPool = pool;

        s_logger.LogDebug("Created dedicated presentation device and queue");
    }

    private void CreateSyncObjects()
    {
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };

        VkSemaphore imageAvailable, renderFinished;
        Fence fence;

        var result = _vk.CreateSemaphore(_device, &semaphoreInfo, null, &imageAvailable);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create imageAvailable semaphore: {result}");

        result = _vk.CreateSemaphore(_device, &semaphoreInfo, null, &renderFinished);
        if (result != Result.Success)
        {
            _vk.DestroySemaphore(_device, imageAvailable, null);
            throw new InvalidOperationException($"Failed to create renderFinished semaphore: {result}");
        }

        result = _vk.CreateFence(_device, &fenceInfo, null, &fence);
        if (result != Result.Success)
        {
            _vk.DestroySemaphore(_device, imageAvailable, null);
            _vk.DestroySemaphore(_device, renderFinished, null);
            throw new InvalidOperationException($"Failed to create fence: {result}");
        }

        _imageAvailableSemaphore = imageAvailable;
        _renderFinishedSemaphore = renderFinished;
        _inFlightFence = fence;
    }

    private void CreateSourceImage(int width, int height, Format format)
    {
        CreateSampledImage(width, height, format, out _sourceImage, out _sourceMemory, out _sourceImageView);
        _sourceImageLayout = ImageLayout.Undefined;

        _descriptorSet = _pipeline!.AllocateDescriptorSet();
        _pipeline.UpdateDescriptorSet(_descriptorSet, VulkanPresentPipeline.SourceBinding, _sourceImageView);
        // The shader samples the overlay only once there is one, but its binding has to hold an image all along.
        _pipeline.UpdateDescriptorSet(_descriptorSet, VulkanPresentPipeline.OverlayBinding,
            _overlayImageView.Handle != 0 ? _overlayImageView : _sourceImageView);
    }

    private void CreateOverlayImage(int width, int height)
    {
        CreateSampledImage(width, height, Format.B8G8R8A8Unorm, out _overlayImage, out _overlayMemory, out _overlayImageView);
        _overlayImageLayout = ImageLayout.Undefined;
        _overlayWidth = width;
        _overlayHeight = height;

        if (_descriptorSet.Handle != 0)
            _pipeline!.UpdateDescriptorSet(_descriptorSet, VulkanPresentPipeline.OverlayBinding, _overlayImageView);
    }

    private void DestroyOverlayImage()
    {
        DestroySampledImage(ref _overlayImage, ref _overlayMemory, ref _overlayImageView);

        _overlayWidth = 0;
        _overlayHeight = 0;
        _overlayImageLayout = ImageLayout.Undefined;
        _overlayShown = false;

        // The binding must not keep the view just destroyed, in case no new overlay image follows.
        if (_descriptorSet.Handle != 0 && _sourceImageView.Handle != 0 && _pipeline != null)
            _pipeline.UpdateDescriptorSet(_descriptorSet, VulkanPresentPipeline.OverlayBinding, _sourceImageView);
    }

    // A device-local image the present shader samples, filled from a staging buffer.
    private void CreateSampledImage(int width, int height, Format format,
        out Image image, out DeviceMemory memory, out ImageView view)
    {
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        Image newImage;
        Check(_vk.CreateImage(_device, &imageInfo, null, &newImage), "vkCreateImage");
        image = newImage;
        memory = default;
        view = default;

        MemoryRequirements memReqs;
        _vk.GetImageMemoryRequirements(_device, newImage, &memReqs);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        DeviceMemory newMemory;
        Check(_vk.AllocateMemory(_device, &allocInfo, null, &newMemory), "vkAllocateMemory");
        memory = newMemory;
        Check(_vk.BindImageMemory(_device, newImage, newMemory, 0), "vkBindImageMemory");

        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = newImage,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        ImageView newView;
        Check(_vk.CreateImageView(_device, &viewInfo, null, &newView), "vkCreateImageView");
        view = newView;
    }

    private void DestroySampledImage(ref Image image, ref DeviceMemory memory, ref ImageView view)
    {
        if (view.Handle != 0)
        {
            _vk.DestroyImageView(_device, view, null);
            view = default;
        }

        if (image.Handle != 0)
        {
            _vk.DestroyImage(_device, image, null);
            image = default;
        }

        if (memory.Handle != 0)
        {
            _vk.FreeMemory(_device, memory, null);
            memory = default;
        }
    }

    private void DestroySourceImage()
    {
        _pipeline?.FreeDescriptorSet(_descriptorSet);
        _descriptorSet = default;

        DestroySampledImage(ref _sourceImage, ref _sourceMemory, ref _sourceImageView);

        _sourceWidth = 0;
        _sourceHeight = 0;
        _sourceImageLayout = ImageLayout.Undefined;
    }

    private void EnsureStagingBuffer(ulong requiredSize)
    {
        if (_stagingSize >= requiredSize)
            return;

        DestroyStagingBuffer();
        CreateStagingBuffer(requiredSize, out _stagingBuffer, out _stagingMemory);
        _stagingSize = requiredSize;
    }

    private void DestroyStagingBuffer()
    {
        DestroyStagingBuffer(ref _stagingBuffer, ref _stagingMemory);
        _stagingSize = 0;
    }

    private void EnsureOverlayStagingBuffer(ulong requiredSize)
    {
        if (_overlayStagingSize >= requiredSize)
            return;

        DestroyOverlayStagingBuffer();
        CreateStagingBuffer(requiredSize, out _overlayStagingBuffer, out _overlayStagingMemory);
        _overlayStagingSize = requiredSize;
    }

    private void DestroyOverlayStagingBuffer()
    {
        DestroyStagingBuffer(ref _overlayStagingBuffer, ref _overlayStagingMemory);
        _overlayStagingSize = 0;
    }

    private void CreateStagingBuffer(ulong size, out Silk.NET.Vulkan.Buffer buffer, out DeviceMemory memory)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive
        };

        Silk.NET.Vulkan.Buffer newBuffer;
        Check(_vk.CreateBuffer(_device, &bufferInfo, null, &newBuffer), "vkCreateBuffer");
        buffer = newBuffer;
        memory = default;

        MemoryRequirements memReqs;
        _vk.GetBufferMemoryRequirements(_device, newBuffer, &memReqs);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        DeviceMemory newMemory;
        Check(_vk.AllocateMemory(_device, &allocInfo, null, &newMemory), "vkAllocateMemory");
        memory = newMemory;
        Check(_vk.BindBufferMemory(_device, newBuffer, newMemory, 0), "vkBindBufferMemory");
    }

    private void DestroyStagingBuffer(ref Silk.NET.Vulkan.Buffer buffer, ref DeviceMemory memory)
    {
        if (buffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, buffer, null);
            buffer = default;
        }

        if (memory.Handle != 0)
        {
            _vk.FreeMemory(_device, memory, null);
            memory = default;
        }
    }

    // The CPU copy of the overlay that Skia draws into, kept between draws.
    private void EnsureOverlayPixels(ulong size)
    {
        if (_overlayPixelsSize >= size)
            return;

        FreeOverlayPixels();
        _overlayPixels = System.Runtime.InteropServices.NativeMemory.AlignedAlloc((nuint)size, 64);
        _overlayPixelsSize = size;
    }

    private void FreeOverlayPixels()
    {
        if (_overlayPixels != null)
        {
            System.Runtime.InteropServices.NativeMemory.AlignedFree(_overlayPixels);
            _overlayPixels = null;
        }

        _overlayPixelsSize = 0;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
        => VulkanPhysicalDeviceQueries.FindMemoryType(_vk, _physicalDevice, typeFilter, properties);

    private CommandBuffer AllocateCommandBuffer()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer cmdBuf;
        Check(_vk.AllocateCommandBuffers(_device, &allocInfo, &cmdBuf), "vkAllocateCommandBuffers");
        return cmdBuf;
    }

    private void BeginCommandBuffer(CommandBuffer cmdBuf)
    {
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        Check(_vk.BeginCommandBuffer(cmdBuf, &beginInfo), "vkBeginCommandBuffer");
    }

    private void EndAndSubmitUploadCommands(CommandBuffer cmdBuf)
    {
        Check(_vk.EndCommandBuffer(cmdBuf), "vkEndCommandBuffer");

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmdBuf
        };

        Check(_vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, default), "vkQueueSubmit (HDR source upload)");
    }

    private void TransitionImageLayout(CommandBuffer cmdBuf, Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        PipelineStageFlags srcStage, dstStage;
        AccessFlags srcAccess, dstAccess;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.TransferDstOptimal)
        {
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.TransferBit;
            srcAccess = 0;
            dstAccess = AccessFlags.TransferWriteBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.FragmentShaderBit;
            srcAccess = AccessFlags.TransferWriteBit;
            dstAccess = AccessFlags.ShaderReadBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.TransferDstOptimal)
        {
            srcStage = PipelineStageFlags.FragmentShaderBit;
            dstStage = PipelineStageFlags.TransferBit;
            srcAccess = AccessFlags.ShaderReadBit;
            dstAccess = AccessFlags.TransferWriteBit;
        }
        else
        {
            srcStage = PipelineStageFlags.AllCommandsBit;
            dstStage = PipelineStageFlags.AllCommandsBit;
            srcAccess = AccessFlags.MemoryWriteBit;
            dstAccess = AccessFlags.MemoryReadBit;
        }

        var barrier = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            SrcAccessMask = srcAccess,
            DstAccessMask = dstAccess
        };

        _vk.CmdPipelineBarrier(cmdBuf, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
    }

    private static Format BitmapColorTypeToVkFormat(BitmapColorType colorType)
    {
        return colorType switch
        {
            BitmapColorType.RgbaF16 => Format.R16G16B16A16Sfloat,
            BitmapColorType.RgbaF32 => Format.R32G32B32A32Sfloat,
            BitmapColorType.Rgba8888 => Format.R8G8B8A8Unorm,
            BitmapColorType.Bgra8888 => Format.B8G8R8A8Unorm,
            BitmapColorType.Rgba16161616 => Format.R16G16B16A16Unorm,
            BitmapColorType.Srgba8888 => Format.R8G8B8A8Srgb,
            _ => Format.R8G8B8A8Unorm
        };
    }
}
