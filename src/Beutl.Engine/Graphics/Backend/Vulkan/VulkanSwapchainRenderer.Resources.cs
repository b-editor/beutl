using System.Runtime.InteropServices;
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
        // Find graphics queue family
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* pQueueFamilies = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, pQueueFamilies);
        }

        _queueFamilyIndex = uint.MaxValue;
        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                _queueFamilyIndex = i;
                break;
            }
        }

        if (_queueFamilyIndex == uint.MaxValue)
            throw new InvalidOperationException("No graphics queue family found");

        // Create device
        float queuePriority = 1.0f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _queueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        // Required extensions
        var extensions = new List<string> { "VK_KHR_swapchain" };
        if (OperatingSystem.IsMacOS())
        {
            // Check for portability subset
            uint extCount = 0;
            _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &extCount, null);
            var availableExtensions = new ExtensionProperties[extCount];
            fixed (ExtensionProperties* pExtensions = availableExtensions)
            {
                _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &extCount, pExtensions);
            }

            foreach (var ext in availableExtensions)
            {
                var name = Marshal.PtrToStringAnsi((IntPtr)ext.ExtensionName);
                if (name == "VK_KHR_portability_subset")
                {
                    extensions.Add("VK_KHR_portability_subset");
                    break;
                }
            }
        }

        var extensionPtrs = new byte*[extensions.Count];
        for (int i = 0; i < extensions.Count; i++)
            extensionPtrs[i] = (byte*)Marshal.StringToHGlobalAnsi(extensions[i]);

        try
        {
            var features = new PhysicalDeviceFeatures();
            fixed (byte** ppExtensions = extensionPtrs)
            {
                var createInfo = new DeviceCreateInfo
                {
                    SType = StructureType.DeviceCreateInfo,
                    QueueCreateInfoCount = 1,
                    PQueueCreateInfos = &queueCreateInfo,
                    EnabledExtensionCount = (uint)extensions.Count,
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
        finally
        {
            foreach (var ptr in extensionPtrs)
                Marshal.FreeHGlobal((IntPtr)ptr);
        }

        _vk.GetDeviceQueue(_device, _queueFamilyIndex, 0, out _graphicsQueue);

        // Create command pool
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

        Image image;
        _vk.CreateImage(_device, &imageInfo, null, &image);
        _sourceImage = image;

        // Allocate memory
        MemoryRequirements memReqs;
        _vk.GetImageMemoryRequirements(_device, _sourceImage, &memReqs);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        DeviceMemory memory;
        _vk.AllocateMemory(_device, &allocInfo, null, &memory);
        _sourceMemory = memory;
        _vk.BindImageMemory(_device, _sourceImage, _sourceMemory, 0);

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _sourceImage,
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

        ImageView view;
        _vk.CreateImageView(_device, &viewInfo, null, &view);
        _sourceImageView = view;
        _sourceImageLayout = ImageLayout.Undefined;

        // Allocate descriptor set
        _descriptorSet = _pipeline!.AllocateDescriptorSet();
    }

    private void DestroySourceImage()
    {
        if (_descriptorSet.Handle != 0)
        {
            _pipeline?.FreeDescriptorSet(_descriptorSet);
            _descriptorSet = default;
        }

        if (_sourceImageView.Handle != 0)
        {
            _vk.DestroyImageView(_device, _sourceImageView, null);
            _sourceImageView = default;
        }

        if (_sourceImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _sourceImage, null);
            _sourceImage = default;
        }

        if (_sourceMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _sourceMemory, null);
            _sourceMemory = default;
        }

        _sourceWidth = 0;
        _sourceHeight = 0;
        _sourceImageLayout = ImageLayout.Undefined;
    }

    private void EnsureStagingBuffer(ulong requiredSize)
    {
        if (_stagingSize >= requiredSize)
            return;

        DestroyStagingBuffer();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = requiredSize,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive
        };

        Silk.NET.Vulkan.Buffer buffer;
        _vk.CreateBuffer(_device, &bufferInfo, null, &buffer);
        _stagingBuffer = buffer;

        MemoryRequirements memReqs;
        _vk.GetBufferMemoryRequirements(_device, _stagingBuffer, &memReqs);

        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(memReqs.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        DeviceMemory memory;
        _vk.AllocateMemory(_device, &allocInfo, null, &memory);
        _stagingMemory = memory;
        _vk.BindBufferMemory(_device, _stagingBuffer, _stagingMemory, 0);

        _stagingSize = requiredSize;
    }

    private void DestroyStagingBuffer()
    {
        if (_stagingBuffer.Handle != 0)
        {
            _vk.DestroyBuffer(_device, _stagingBuffer, null);
            _stagingBuffer = default;
        }

        if (_stagingMemory.Handle != 0)
        {
            _vk.FreeMemory(_device, _stagingMemory, null);
            _stagingMemory = default;
        }

        _stagingSize = 0;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProps;
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, &memProps);

        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Failed to find suitable memory type");
    }

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
        _vk.AllocateCommandBuffers(_device, &allocInfo, &cmdBuf);
        return cmdBuf;
    }

    private void BeginCommandBuffer(CommandBuffer cmdBuf)
    {
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(cmdBuf, &beginInfo);
    }

    private void EndAndSubmitUploadCommands(CommandBuffer cmdBuf)
    {
        _vk.EndCommandBuffer(cmdBuf);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmdBuf
        };

        Result result = _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, default);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to submit HDR source upload: {result}");
        }
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

    private static int BitmapColorTypeBytesPerPixel(BitmapColorType colorType)
    {
        return colorType switch
        {
            BitmapColorType.RgbaF16 => 8,
            BitmapColorType.RgbaF32 => 16,
            BitmapColorType.Rgba8888 => 4,
            BitmapColorType.Bgra8888 => 4,
            BitmapColorType.Rgba16161616 => 8,
            BitmapColorType.Srgba8888 => 4,
            _ => 4
        };
    }
}
