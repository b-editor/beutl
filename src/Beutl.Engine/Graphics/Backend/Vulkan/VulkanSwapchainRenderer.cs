using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

/// <summary>
/// Parameters for bitmap rendering.
/// </summary>
internal readonly record struct RenderParams(
    float SourceWidth,
    float SourceHeight,
    float DestWidth,
    float DestHeight,
    Stretch Stretch,
    UIToneMappingOperator ToneMapping,
    float Exposure,
    bool IsSourceLinear);

/// <summary>
/// Orchestrates Vulkan swapchain rendering with a dedicated presentation thread and device.
/// </summary>
internal sealed unsafe class VulkanSwapchainRenderer : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanSwapchainRenderer>();

    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly PhysicalDevice _physicalDevice;

    // Dedicated presentation device (independent from existing VulkanContext)
    private Device _device;
    private Queue _graphicsQueue;
    private uint _queueFamilyIndex;
    private CommandPool _commandPool;

    // Swapchain infrastructure
    private VulkanSwapchain? _swapchain;
    private VulkanPresentPipeline? _pipeline;
    private SurfaceKHR _surface;

    // Source texture
    private Image _sourceImage;
    private DeviceMemory _sourceMemory;
    private ImageView _sourceImageView;
    private DescriptorSet _descriptorSet;
    private int _sourceWidth;
    private int _sourceHeight;
    private Format _sourceFormat;

    // Staging buffer
    private Silk.NET.Vulkan.Buffer _stagingBuffer;
    private DeviceMemory _stagingMemory;
    private ulong _stagingSize;

    // Pre-allocated render command buffer (reused each frame)
    private CommandBuffer _renderCommandBuffer;

    // Synchronization
    private VkSemaphore _imageAvailableSemaphore;
    private VkSemaphore _renderFinishedSemaphore;
    private Fence _inFlightFence;

    // Presentation thread
    private Thread? _presentThread;
    private readonly BlockingCollection<RenderCommand> _commandQueue = new(4);
    private volatile bool _running;
    private bool _disposed;

    public VulkanSwapchainRenderer()
    {
        var vulkanInstance = GraphicsContextFactory.VulkanInstance
            ?? throw new InvalidOperationException("Vulkan instance is not available");

        _vk = vulkanInstance.Vk;
        _instance = vulkanInstance.Instance;

        var gpuDetails = GraphicsContextFactory.GetSelectedGpuDetails()
            ?? vulkanInstance.SelectBestPhysicalDevice();

        _physicalDevice = gpuDetails.Device;

        CreateDedicatedDevice();
        CreateSyncObjects();
    }

    public bool IsHdrActive => _swapchain?.IsHdr ?? false;

    public void Initialize(IntPtr nativeHandle, string handleDescriptor, uint width, uint height)
    {
        _surface = VulkanSurfaceHelper.CreateSurface(_vk, _instance, nativeHandle, handleDescriptor);
        _swapchain = new VulkanSwapchain(_vk, _instance, _physicalDevice, _device, _queueFamilyIndex, _surface, width, height);
        _pipeline = new VulkanPresentPipeline(_vk, _device, _swapchain.Format, _swapchain.ImageViews, _swapchain.Extent);
        _renderCommandBuffer = AllocateCommandBuffer();

        StartPresentThread();

        s_logger.LogInformation("VulkanSwapchainRenderer initialized: HDR={IsHdr}", _swapchain.IsHdr);
    }

    public void Resize(uint width, uint height)
    {
        if (_swapchain == null || _pipeline == null || _disposed)
            return;

        if (width == 0 || height == 0)
            return;

        // Dispatch resize to presentation thread
        _commandQueue.TryAdd(new RenderCommand.ResizeCommand(width, height));
    }

    public void RequestRender(Ref<Bitmap> bitmapRef, RenderParams renderParams)
    {
        if (_disposed)
        {
            bitmapRef.Dispose();
            return;
        }

        if (!_commandQueue.TryAdd(new RenderCommand.DrawCommand(bitmapRef, renderParams)))
            bitmapRef.Dispose();
    }

    private void StartPresentThread()
    {
        _running = true;
        _presentThread = new Thread(PresentThreadLoop)
        {
            Name = "Beutl.PresentThread",
            IsBackground = true
        };
        _presentThread.Start();
    }

    private void PresentThreadLoop()
    {
        try
        {
            while (_running)
            {
                if (!_commandQueue.TryTake(out var command, 100))
                    continue;

                // Drain queue to get latest command, dispose intermediate bitmaps
                while (_commandQueue.TryTake(out var newer))
                {
                    if (command is RenderCommand.DrawCommand oldDraw)
                        oldDraw.BitmapRef.Dispose();

                    command = newer;
                }

                try
                {
                    switch (command)
                    {
                        case RenderCommand.DrawCommand draw:
                            ExecuteRender(draw.BitmapRef, draw.Params);
                            draw.BitmapRef.Dispose();
                            break;

                        case RenderCommand.ResizeCommand resize:
                            ExecuteResize(resize.Width, resize.Height);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    s_logger.LogError(ex, "Error in presentation thread");
                }
            }
        }
        catch (InvalidOperationException)
        {
            // BlockingCollection completed
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Presentation thread crashed");
        }
    }

    private void ExecuteResize(uint width, uint height)
    {
        if (_swapchain == null || _pipeline == null)
            return;

        _vk.DeviceWaitIdle(_device);
        _swapchain.Recreate(width, height);
        _pipeline.RecreateFramebuffers(_swapchain.ImageViews, _swapchain.Extent);

        s_logger.LogDebug("Swapchain resized to {Width}x{Height}", width, height);
    }

    private void ExecuteRender(Ref<Bitmap> bitmapRef, RenderParams renderParams, int retryCount = 0)
    {
        if (retryCount > 10) return;

        if (_swapchain == null || _pipeline == null)
            return;

        var bitmap = bitmapRef.Value;
        if (bitmap.IsDisposed)
            return;

        // Upload bitmap to GPU
        UploadBitmap(bitmap);

        // Acquire next swapchain image
        var fence = _inFlightFence;
        _vk.WaitForFences(_device, 1, &fence, Vk.True, ulong.MaxValue);
        _vk.ResetFences(_device, 1, &fence);

        var acquireResult = _swapchain.AcquireNextImage(_imageAvailableSemaphore, out uint imageIndex);
        if (acquireResult == Result.ErrorOutOfDateKhr)
        {
            ExecuteResize(_swapchain.Extent.Width, _swapchain.Extent.Height);
            return;
        }

        // Record and submit command buffer
        RecordAndSubmit(imageIndex, renderParams);

        // Present
        var presentResult = _swapchain.Present(_graphicsQueue, _renderFinishedSemaphore, imageIndex);
        if (presentResult is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
        {
            ExecuteResize(_swapchain.Extent.Width, _swapchain.Extent.Height);
            ExecuteRender(bitmapRef, renderParams, ++retryCount); // Retry render after resize
        }
    }

    private void UploadBitmap(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        var vkFormat = BitmapColorTypeToVkFormat(bitmap.ColorType);

        // Recreate source image if dimensions/format changed
        if (width != _sourceWidth || height != _sourceHeight || vkFormat != _sourceFormat)
        {
            DestroySourceImage();
            CreateSourceImage(width, height, vkFormat);
            _sourceWidth = width;
            _sourceHeight = height;
            _sourceFormat = vkFormat;
        }

        // Upload pixel data via staging buffer
        ulong dataSize = (ulong)(bitmap.RowBytes * height);
        EnsureStagingBuffer(dataSize);

        // Map and copy
        void* mapped;
        _vk.MapMemory(_device, _stagingMemory, 0, dataSize, 0, &mapped);
        System.Buffer.MemoryCopy((void*)bitmap.Data, mapped, (long)dataSize, (long)dataSize);
        _vk.UnmapMemory(_device, _stagingMemory);

        // Copy staging buffer to image
        var cmdBuf = AllocateCommandBuffer();
        BeginCommandBuffer(cmdBuf);

        // Transition to transfer dst
        TransitionImageLayout(cmdBuf, _sourceImage, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = (uint)(bitmap.RowBytes / BitmapColorTypeBytesPerPixel(bitmap.ColorType)),
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D((uint)width, (uint)height, 1)
        };

        _vk.CmdCopyBufferToImage(cmdBuf, _stagingBuffer, _sourceImage, ImageLayout.TransferDstOptimal, 1, &region);

        // Transition to shader read
        TransitionImageLayout(cmdBuf, _sourceImage, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        EndAndSubmitCommandBuffer(cmdBuf);

        // Update descriptor set
        _pipeline!.UpdateDescriptorSet(_descriptorSet, _sourceImageView);
    }

    private void RecordAndSubmit(uint imageIndex, RenderParams renderParams)
    {
        var cmdBuf = _renderCommandBuffer;
        _vk.ResetCommandBuffer(cmdBuf, 0);
        BeginCommandBuffer(cmdBuf);

        var extent = _swapchain!.Extent;

        // Calculate viewport rects for stretch mode
        ComputeStretchRects(renderParams, extent, out var pushConstants);
        pushConstants.IsHdr = _swapchain.IsHdr && renderParams.IsSourceLinear ? 1 : 0;

        // Begin render pass
        var clearValue = new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 1f) };
        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _pipeline!.RenderPassHandle,
            Framebuffer = _pipeline.Framebuffers[imageIndex],
            RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = extent },
            ClearValueCount = 1,
            PClearValues = &clearValue
        };

        _vk.CmdBeginRenderPass(cmdBuf, &renderPassInfo, SubpassContents.Inline);
        _vk.CmdBindPipeline(cmdBuf, PipelineBindPoint.Graphics, _pipeline.PipelineHandle);

        // Dynamic viewport and scissor
        var viewport = new Viewport { X = 0, Y = 0, Width = extent.Width, Height = extent.Height, MinDepth = 0, MaxDepth = 1 };
        var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = extent };
        _vk.CmdSetViewport(cmdBuf, 0, 1, &viewport);
        _vk.CmdSetScissor(cmdBuf, 0, 1, &scissor);

        // Bind descriptor set
        fixed (DescriptorSet* pSet = &_descriptorSet)
        {
            _vk.CmdBindDescriptorSets(cmdBuf, PipelineBindPoint.Graphics, _pipeline.PipelineLayoutHandle, 0, 1, pSet, 0, null);
        }

        // Push constants
        _vk.CmdPushConstants(cmdBuf, _pipeline.PipelineLayoutHandle, ShaderStageFlags.FragmentBit, 0, (uint)sizeof(PresentPushConstants), &pushConstants);

        // Draw fullscreen triangle
        _vk.CmdDraw(cmdBuf, 3, 1, 0, 0);

        _vk.CmdEndRenderPass(cmdBuf);
        _vk.EndCommandBuffer(cmdBuf);

        // Submit
        var waitSemaphore = _imageAvailableSemaphore;
        var signalSemaphore = _renderFinishedSemaphore;
        PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &cmdBuf,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore
        };

        _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlightFence);
    }

    private static void ComputeStretchRects(RenderParams p, Extent2D extent, out PresentPushConstants pc)
    {
        pc = default;
        pc.Exposure = p.Exposure;
        pc.TmOperator = (int)p.ToneMapping;

        // Source rect in UV space (full texture)
        pc.SrcX = 0; pc.SrcY = 0; pc.SrcW = 1; pc.SrcH = 1;

        // Destination rect in UV space based on stretch mode
        float destW = extent.Width;
        float destH = extent.Height;
        float srcW = p.SourceWidth;
        float srcH = p.SourceHeight;

        if (srcW <= 0 || srcH <= 0 || destW <= 0 || destH <= 0)
        {
            pc.DstX = 0; pc.DstY = 0; pc.DstW = 1; pc.DstH = 1;
            return;
        }

        float scaleX, scaleY;
        switch (p.Stretch)
        {
            case Stretch.None:
                scaleX = srcW / destW;
                scaleY = srcH / destH;
                break;
            case Stretch.Fill:
                scaleX = 1;
                scaleY = 1;
                break;
            case Stretch.Uniform:
                float scale = Math.Min(destW / srcW, destH / srcH);
                scaleX = srcW * scale / destW;
                scaleY = srcH * scale / destH;
                break;
            case Stretch.UniformToFill:
                float scaleFill = Math.Max(destW / srcW, destH / srcH);
                scaleX = srcW * scaleFill / destW;
                scaleY = srcH * scaleFill / destH;
                break;
            default:
                scaleX = 1; scaleY = 1;
                break;
        }

        pc.DstX = (1 - scaleX) / 2;
        pc.DstY = (1 - scaleY) / 2;
        pc.DstW = scaleX;
        pc.DstH = scaleY;
    }

    #region Vulkan Resource Management

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

    private void EndAndSubmitCommandBuffer(CommandBuffer cmdBuf)
    {
        _vk.EndCommandBuffer(cmdBuf);

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmdBuf
        };

        var fence = _inFlightFence;
        _vk.WaitForFences(_device, 1, &fence, Vk.True, ulong.MaxValue);
        _vk.ResetFences(_device, 1, &fence);
        _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlightFence);
        _vk.WaitForFences(_device, 1, &fence, Vk.True, ulong.MaxValue);

        _vk.FreeCommandBuffers(_device, _commandPool, 1, &cmdBuf);
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

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop presentation thread
        _running = false;
        _commandQueue.CompleteAdding();
        _presentThread?.Join(2000);

        // Drain remaining commands
        while (_commandQueue.TryTake(out var cmd))
        {
            if (cmd is RenderCommand.DrawCommand draw)
                draw.BitmapRef.Dispose();
        }

        _commandQueue.Dispose();

        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);

            DestroySourceImage();
            DestroyStagingBuffer();

            _pipeline?.Dispose();
            _swapchain?.Dispose();

            if (_surface.Handle != 0)
                VulkanSurfaceHelper.DestroySurface(_vk, _instance, _surface);

            if (_imageAvailableSemaphore.Handle != 0)
                _vk.DestroySemaphore(_device, _imageAvailableSemaphore, null);
            if (_renderFinishedSemaphore.Handle != 0)
                _vk.DestroySemaphore(_device, _renderFinishedSemaphore, null);
            if (_inFlightFence.Handle != 0)
                _vk.DestroyFence(_device, _inFlightFence, null);
            if (_renderCommandBuffer.Handle != 0)
            {
                var renderCmdBuf = _renderCommandBuffer;
                _vk.FreeCommandBuffers(_device, _commandPool, 1, &renderCmdBuf);
            }

            if (_commandPool.Handle != 0)
                _vk.DestroyCommandPool(_device, _commandPool, null);

            _vk.DestroyDevice(_device, null);
        }
    }

    private abstract record RenderCommand
    {
        public sealed record DrawCommand(Ref<Bitmap> BitmapRef, RenderParams Params) : RenderCommand;
        public sealed record ResizeCommand(uint Width, uint Height) : RenderCommand;
    }
}
