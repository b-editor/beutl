using System.Collections.Concurrent;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

/// <summary>
/// Orchestrates Vulkan swapchain rendering with a dedicated presentation thread and device.
/// </summary>
internal sealed unsafe partial class VulkanSwapchainRenderer : IDisposable
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
    private VulkanSurfaceInfo _surfaceInfo;

    // Source texture
    private Image _sourceImage;
    private DeviceMemory _sourceMemory;
    private ImageView _sourceImageView;
    private DescriptorSet _descriptorSet;
    private int _sourceWidth;
    private int _sourceHeight;
    private Format _sourceFormat;
    private ImageLayout _sourceImageLayout = ImageLayout.Undefined;

    // Staging buffer
    private Silk.NET.Vulkan.Buffer _stagingBuffer;
    private DeviceMemory _stagingMemory;
    private ulong _stagingSize;

    // Pre-allocated command buffers (reused each frame)
    private CommandBuffer _renderCommandBuffer;
    private CommandBuffer _uploadCommandBuffer;

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
        _surfaceInfo = VulkanSurfaceHelper.CreateSurface(_vk, _instance, nativeHandle, handleDescriptor);
        _swapchain = new VulkanSwapchain(_vk, _instance, _physicalDevice, _device, _queueFamilyIndex, _surfaceInfo.Surface, width, height);
        _pipeline = new VulkanPresentPipeline(_vk, _device, _swapchain.Format, _swapchain.ImageViews, _swapchain.Extent);
        _renderCommandBuffer = AllocateCommandBuffer();
        _uploadCommandBuffer = AllocateCommandBuffer();

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

        // The previous frame fence covers its upload and present draw because both use this queue.
        // Wait before rewriting the persistent staging buffer or replacing the source image.
        var fence = _inFlightFence;
        _vk.WaitForFences(_device, 1, &fence, Vk.True, ulong.MaxValue);

        // The following render submission is ordered after this upload on the same queue, so it
        // needs no per-operation CPU fence wait.
        UploadBitmap(bitmap);

        var acquireResult = _swapchain.AcquireNextImage(_imageAvailableSemaphore, out uint imageIndex);
        if (acquireResult == Result.ErrorOutOfDateKhr)
        {
            ExecuteResize(_swapchain.Extent.Width, _swapchain.Extent.Height);
            return;
        }

        _vk.ResetFences(_device, 1, &fence);

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
        var cmdBuf = _uploadCommandBuffer;
        _vk.ResetCommandBuffer(cmdBuf, 0);
        BeginCommandBuffer(cmdBuf);

        // Transition to transfer dst
        TransitionImageLayout(cmdBuf, _sourceImage, _sourceImageLayout, ImageLayout.TransferDstOptimal);
        _sourceImageLayout = ImageLayout.TransferDstOptimal;

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
        _sourceImageLayout = ImageLayout.ShaderReadOnlyOptimal;

        EndAndSubmitUploadCommands(cmdBuf);

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

        // Srgbフォーマットなら変換は不要。Unorm系は必要
        pushConstants.LinearToSrgb = !_swapchain.SrgbFormat ? 1 : 0;
        pushConstants.IsSourceLinear = renderParams.IsSourceLinear ? 1 : 0;

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

            if (_surfaceInfo.Surface.Handle != 0)
                VulkanSurfaceHelper.DestroySurface(_vk, _instance, _surfaceInfo);

            if (_imageAvailableSemaphore.Handle != 0)
                _vk.DestroySemaphore(_device, _imageAvailableSemaphore, null);
            if (_renderFinishedSemaphore.Handle != 0)
                _vk.DestroySemaphore(_device, _renderFinishedSemaphore, null);
            if (_inFlightFence.Handle != 0)
                _vk.DestroyFence(_device, _inFlightFence, null);
            if (_renderCommandBuffer.Handle != 0 || _uploadCommandBuffer.Handle != 0)
            {
                var cmdBufs = stackalloc CommandBuffer[2];
                int count = 0;
                if (_renderCommandBuffer.Handle != 0)
                    cmdBufs[count++] = _renderCommandBuffer;
                if (_uploadCommandBuffer.Handle != 0)
                    cmdBufs[count++] = _uploadCommandBuffer;
                _vk.FreeCommandBuffers(_device, _commandPool, (uint)count, cmdBufs);
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
