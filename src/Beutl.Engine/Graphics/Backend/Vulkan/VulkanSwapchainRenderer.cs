using System.Collections.Concurrent;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using SkiaSharp;
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

    private Device _device;
    private Queue _graphicsQueue;
    private uint _queueFamilyIndex;
    private CommandPool _commandPool;

    private VulkanSwapchain? _swapchain;
    private VulkanPresentPipeline? _pipeline;
    private VulkanSurfaceInfo _surfaceInfo;

    private Image _sourceImage;
    private DeviceMemory _sourceMemory;
    private ImageView _sourceImageView;
    private DescriptorSet _descriptorSet;
    private int _sourceWidth;
    private int _sourceHeight;
    private Format _sourceFormat;
    private ImageLayout _sourceImageLayout = ImageLayout.Undefined;

    private Silk.NET.Vulkan.Buffer _stagingBuffer;
    private DeviceMemory _stagingMemory;
    private ulong _stagingSize;

    private CommandBuffer _renderCommandBuffer;
    private CommandBuffer _uploadCommandBuffer;
    private CommandBuffer _overlayCommandBuffer;

    // What the UI draws over the frame, which the presentation thread draws at the swapchain's size into the overlay
    // image. The view hands a new one over at any time; only the presentation thread touches the others.
    private OverlayContent? _overlay;
    private bool _overlayDirty;
    private bool _overlayShown;
    private Extent2D _overlayExtent;
    private Image _overlayImage;
    private DeviceMemory _overlayMemory;
    private ImageView _overlayImageView;
    private int _overlayWidth;
    private int _overlayHeight;
    private ImageLayout _overlayImageLayout = ImageLayout.Undefined;
    private Silk.NET.Vulkan.Buffer _overlayStagingBuffer;
    private DeviceMemory _overlayStagingMemory;
    private ulong _overlayStagingSize;
    private void* _overlayPixels;
    private ulong _overlayPixelsSize;

    // What the view asked for and the presentation thread has not taken yet: a new overlay, and parameters to draw the
    // last frame again with. Requests are stamped in the order the view made them, so the newest parameters win over
    // a frame queued before them.
    private readonly Lock _pendingLock = new();
    private OverlayContent? _pendingOverlay;
    private (RenderParams Params, long Stamp)? _pendingRedraw;
    private long _requestStamp;

    // The newest parameters any batch has taken. Only the presentation thread touches it.
    private NewestRenderParams _newestParams;

    private VkSemaphore _imageAvailableSemaphore;
    private VkSemaphore _renderFinishedSemaphore;
    private Fence _inFlightFence;

    private Thread? _presentThread;
    private readonly BlockingCollection<RenderCommand> _commandQueue = new(4);
    private volatile bool _running;
    private bool _disposed;

    // Bounds the wait for the previous frame, so a stalled GPU drops frames instead of blocking the
    // presentation thread; a lost device still fails the wait and stops presenting.
    private const ulong FenceTimeoutNanoseconds = 5_000_000_000;

    // Set on the presentation thread once a Vulkan call fails. A failed submit leaves the in-flight fence
    // unsignaled, so later frames are dropped instead of waiting on it; recreating the view recovers.
    private bool _faulted;

    // Windows only: the display the window is on, which an HDR swapchain is fitted to.
    private WindowsDisplayLuminance? _windowsDisplay;

    // What the last uploaded frame was drawn with, to draw it again for a changed display.
    private RenderParams? _lastRenderParams;

    // The size the last shown frame was drawn at, to draw it again once the surface takes another size.
    private Extent2D _presentedExtent;

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

    /// <summary>Whether the swapchain takes linear BT.2020, whose gamut holds any display's.</summary>
    public bool IsBt2020Active => _swapchain?.IsBt2020 ?? false;

    public void Initialize(IntPtr nativeHandle, uint width, uint height)
    {
        _surfaceInfo = VulkanSurfaceHelper.CreateSurface(_vk, _instance, nativeHandle);
        _swapchain = new VulkanSwapchain(_vk, _instance, _physicalDevice, _device, _surfaceInfo.Surface, width, height);
        _pipeline = new VulkanPresentPipeline(_vk, _device, _swapchain.Format, _swapchain.ImageViews, _swapchain.Extent);
        _renderCommandBuffer = AllocateCommandBuffer();
        _uploadCommandBuffer = AllocateCommandBuffer();
        _overlayCommandBuffer = AllocateCommandBuffer();

        if (OperatingSystem.IsWindows())
            _windowsDisplay = new WindowsDisplayLuminance(nativeHandle);

        StartPresentThread();

        s_logger.LogInformation("VulkanSwapchainRenderer initialized: HDR={IsHdr}", _swapchain.IsHdr);
    }

    public void Resize(uint width, uint height)
    {
        if (_swapchain == null || _pipeline == null || _disposed)
            return;

        if (width == 0 || height == 0)
            return;

        _commandQueue.TryAdd(new RenderCommand.ResizeCommand(width, height));
    }

    public void RequestRender(Ref<Bitmap> bitmapRef, RenderParams renderParams)
    {
        if (_disposed)
        {
            bitmapRef.Dispose();
            return;
        }

        long stamp = Interlocked.Increment(ref _requestStamp);
        if (!_commandQueue.TryAdd(new RenderCommand.DrawCommand(bitmapRef, renderParams, stamp)))
            bitmapRef.Dispose();
    }

    /// <summary>
    /// Draws the last frame again with other parameters, such as another destination while the preview is zoomed, without
    /// uploading it again. The newest parameters are kept until the presentation thread takes them, so none is dropped.
    /// </summary>
    public void RequestRedraw(RenderParams renderParams)
    {
        if (_disposed)
            return;

        long stamp = Interlocked.Increment(ref _requestStamp);
        lock (_pendingLock)
            _pendingRedraw = (renderParams, stamp);

        // A full queue means the thread is busy, and it takes the parameters with its next command.
        _commandQueue.TryAdd(RenderCommand.WakeCommand.Instance);
    }

    /// <summary>
    /// Hands over what the UI draws over the frame, recorded in a view <paramref name="width"/> by
    /// <paramref name="height"/> in size, or null for nothing. The renderer owns the picture from here on: the
    /// presentation thread draws it at the swapchain's size and shows it over the frame, a paused frame included.
    /// </summary>
    public void SetOverlay(SKPicture? picture, float width, float height)
    {
        if (_disposed)
        {
            picture?.Dispose();
            return;
        }

        OverlayContent? replaced;
        lock (_pendingLock)
        {
            replaced = _pendingOverlay;
            _pendingOverlay = new OverlayContent(picture, width, height);
        }

        replaced?.Dispose();
        _commandQueue.TryAdd(RenderCommand.WakeCommand.Instance);
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
                var batch = new CommandBatch();
                if (_commandQueue.TryTake(out var command, 100))
                {
                    batch.Add(command);
                    // What queued meanwhile: the newest frame and parameters win, and older frames are released.
                    while (_commandQueue.TryTake(out var newer))
                        batch.Add(newer);
                }

                TakePending(ref batch);

                // A frame comes through the queue and a redraw beside it, so a frame requested before a redraw can reach a
                // later batch than the redraw did. Its older parameters must not move the frame back.
                if (batch.Newest.Params is { } requested)
                {
                    _newestParams.Take(requested, batch.Newest.Stamp);
                    batch.Newest = _newestParams;
                }

                // A paused preview gets no new frame when its window moves to another display, the display's settings
                // change, or its native view takes a new size after the last frame, so the last one is drawn again.
                if (batch.IsEmpty)
                {
                    if (_lastRenderParams is not { } lastParams
                        || !ShouldRedrawPaused(
                            RefreshDisplay(lastParams.DisplayPeakLuminance),
                            _faulted ? null : _swapchain?.GetSurfaceExtent(),
                            _presentedExtent))
                    {
                        continue;
                    }

                    batch.Redraw = true;
                }

                try
                {
                    ExecuteBatch(ref batch);
                }
                catch (VulkanPresentException ex)
                {
                    _faulted = true;
                    s_logger.LogError(ex, "Stopped presenting the preview after a Vulkan call failed");
                }
                catch (Exception ex)
                {
                    s_logger.LogError(ex, "Error in presentation thread");
                }
                finally
                {
                    batch.Bitmap?.Dispose();
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

    private void TakePending(ref CommandBatch batch)
    {
        OverlayContent? overlay;
        (RenderParams Params, long Stamp)? redraw;
        lock (_pendingLock)
        {
            overlay = _pendingOverlay;
            _pendingOverlay = null;
            redraw = _pendingRedraw;
            _pendingRedraw = null;
        }

        if (overlay != null)
        {
            _overlay?.Dispose();
            _overlay = overlay;
            _overlayDirty = true;
            batch.Redraw = true;
        }

        if (redraw is { } r)
        {
            batch.Newest.Take(r.Params, r.Stamp);
            batch.Redraw = true;
        }
    }

    private void ExecuteBatch(ref CommandBatch batch)
    {
        if (batch.Bitmap is { } bitmap)
        {
            // A resize taken with this frame is not run: the swapchain follows the surface's size before drawing, which
            // a resize can leave old, and the frame would stay stretched until the idle redraw.
            FollowSurfaceSize();
            ExecuteRender(bitmap, batch.Newest.Params!.Value);
        }
        else if (batch.Redraw)
        {
            if ((batch.Newest.Params ?? _lastRenderParams) is { } renderParams)
            {
                FollowSurfaceSize();
                ExecuteRender(null, renderParams);
            }
        }
        else if (batch.Resize is { } size)
        {
            ExecuteResize(size.Width, size.Height);
        }
    }

    private void ExecuteResize(uint width, uint height)
    {
        if (_faulted || _swapchain == null || _pipeline == null)
            return;

        Check(_vk.DeviceWaitIdle(_device), "vkDeviceWaitIdle");
        _swapchain.Recreate(width, height);
        _pipeline.RecreateFramebuffers(_swapchain.ImageViews, _swapchain.Extent);

        s_logger.LogDebug("Swapchain resized to {Width}x{Height}", width, height);
    }

    // Without a bitmap, draws the one uploaded last again.
    private void ExecuteRender(Ref<Bitmap>? bitmapRef, RenderParams renderParams, bool redrawIfOutOfDate = true)
    {
        if (_faulted || _swapchain == null || _pipeline == null)
            return;

        var bitmap = bitmapRef?.Value;
        if (bitmap?.IsDisposed == true)
            return;

        // Drawing again needs a frame uploaded before.
        if (bitmap == null && _sourceImage.Handle == 0)
            return;

        // The previous frame fence covers its upload and present draw because both use this queue.
        // Wait before rewriting the persistent staging buffer or replacing the source image.
        var fence = _inFlightFence;
        Result waitResult = _vk.WaitForFences(_device, 1, &fence, Vk.True, FenceTimeoutNanoseconds);
        if (waitResult == Result.Timeout)
        {
            // The GPU may still finish the previous frame, so drop this one and wait again on the next.
            s_logger.LogWarning("Dropped a preview frame because the previous one had not finished");
            return;
        }

        Check(waitResult, "vkWaitForFences");

        if (bitmap != null)
        {
            // The following render submission is ordered after this upload on the same queue, so it
            // needs no per-operation CPU fence wait.
            UploadBitmap(bitmap);
        }

        // What the idle wait draws the frame again with.
        _lastRenderParams = renderParams;

        // The overlay follows the swapchain's size, so a resize draws it again too.
        if (_overlayDirty || !SameSize(_overlayExtent, _swapchain.Extent))
            UploadOverlay();

        RefreshDisplay(renderParams.DisplayPeakLuminance);

        var acquireResult = _swapchain.AcquireNextImage(_imageAvailableSemaphore, out uint imageIndex);
        if (acquireResult == Result.ErrorOutOfDateKhr)
        {
            ExecuteResize(_swapchain.Extent.Width, _swapchain.Extent.Height);
            // Nothing was drawn, and a paused preview sends no later frame, so the uploaded one is drawn once more.
            if (redrawIfOutOfDate)
                ExecuteRender(null, renderParams, redrawIfOutOfDate: false);
            return;
        }

        if (acquireResult != Result.SuboptimalKhr)
            Check(acquireResult, "vkAcquireNextImageKHR");

        Check(_vk.ResetFences(_device, 1, &fence), "vkResetFences");

        RecordAndSubmit(imageIndex, renderParams);

        var presentResult = _swapchain.Present(_graphicsQueue, _renderFinishedSemaphore, imageIndex);
        if (presentResult is Result.Success or Result.SuboptimalKhr)
            _presentedExtent = _swapchain.Extent;

        if (presentResult is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
        {
            ExecuteResize(_swapchain.Extent.Width, _swapchain.Extent.Height);
            // A suboptimal frame was still shown, at the old size, which a paused preview's idle wait draws again;
            // an out-of-date one was not, so it is drawn once more.
            if (presentResult == Result.ErrorOutOfDateKhr && redrawIfOutOfDate)
                ExecuteRender(bitmapRef, renderParams, redrawIfOutOfDate: false);
            return;
        }

        Check(presentResult, "vkQueuePresentKHR");
    }

    /// <summary>
    /// Whether a paused preview draws its last frame again: its display changed, or its surface reports a size the
    /// frame on screen was not drawn at. A resize can run before the native view takes its new size, and acquiring or
    /// presenting reports the change only for a frame after it.
    /// </summary>
    internal static bool ShouldRedrawPaused(bool displayChanged, Extent2D? surface, Extent2D presented)
    {
        return displayChanged || surface is { } size && !SameSize(size, presented);
    }

    /// <summary>
    /// Whether the swapchain has to be rebuilt before drawing: the surface reports a size other than the swapchain's.
    /// A suboptimal present has already rebuilt it, though the frame it showed was drawn at the old size.
    /// </summary>
    internal static bool ShouldRebuild(Extent2D? surface, Extent2D swapchain)
    {
        return surface is { } size && !SameSize(size, swapchain);
    }

    private static bool SameSize(Extent2D a, Extent2D b) => a.Width == b.Width && a.Height == b.Height;

    // Recreates the swapchain at the surface's size when the two differ.
    private void FollowSurfaceSize()
    {
        if (_swapchain?.GetSurfaceExtent() is { } surface && ShouldRebuild(surface, _swapchain.Extent))
            ExecuteResize(surface.Width, surface.Height);
    }

    // Reads the display again for an HDR swapchain on Windows, and returns whether it changed. A peak override in nit
    // stands in for the one the display reports.
    private bool RefreshDisplay(float peakLuminanceOverride)
    {
        if (!OperatingSystem.IsWindows() || _windowsDisplay == null || _swapchain?.IsHdr != true
            || !_windowsDisplay.Refresh(peakLuminanceOverride))
        {
            return false;
        }

        s_logger.LogInformation(
            "Fitting the preview to its display: reference white {ReferenceWhite}, headroom {Headroom}, gamut {Gamut}",
            _windowsDisplay.Current.ReferenceWhite, _windowsDisplay.Current.Headroom,
            _windowsDisplay.Gamut.IsEnabled ? "display" : "none");
        return true;
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
            throw new VulkanPresentException(operation, result);
    }

    private sealed class VulkanPresentException(string operation, Result result)
        : Exception($"{operation} failed with {result}.");

    private void UploadBitmap(Bitmap bitmap)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        var vkFormat = BitmapColorTypeToVkFormat(bitmap.ColorType);

        if (width != _sourceWidth || height != _sourceHeight || vkFormat != _sourceFormat)
        {
            DestroySourceImage();
            CreateSourceImage(width, height, vkFormat);
            _sourceWidth = width;
            _sourceHeight = height;
            _sourceFormat = vkFormat;
        }

        ulong dataSize = (ulong)(bitmap.RowBytes * height);
        EnsureStagingBuffer(dataSize);

        UploadToImage(_uploadCommandBuffer, (void*)bitmap.Data, dataSize, _stagingBuffer, _stagingMemory,
            _sourceImage, ref _sourceImageLayout, (uint)(bitmap.RowBytes / bitmap.BytesPerPixel), width, height);
    }

    // Draws the overlay picture at the swapchain's size, on the CPU as the UI's software renderer would, and uploads it.
    private void UploadOverlay()
    {
        _overlayDirty = false;
        Extent2D extent = _swapchain!.Extent;
        _overlayExtent = extent;
        _overlayShown = false;

        OverlayContent? content = _overlay;
        if (content?.Picture is not { } picture || !(content.Width > 0 && content.Height > 0)
            || extent.Width == 0 || extent.Height == 0)
        {
            return;
        }

        int width = (int)extent.Width;
        int height = (int)extent.Height;
        int rowBytes = width * 4;
        ulong size = (ulong)rowBytes * (ulong)height;

        EnsureOverlayPixels(size);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (SKSurface? surface = SKSurface.Create(info, (IntPtr)_overlayPixels, rowBytes))
        {
            if (surface == null)
                return;

            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(width / content.Width, height / content.Height);
            canvas.DrawPicture(picture);
        }

        if (_overlayImage.Handle == 0 || width != _overlayWidth || height != _overlayHeight)
        {
            DestroyOverlayImage();
            CreateOverlayImage(width, height);
        }

        EnsureOverlayStagingBuffer(size);

        UploadToImage(_overlayCommandBuffer, _overlayPixels, size, _overlayStagingBuffer, _overlayStagingMemory,
            _overlayImage, ref _overlayImageLayout, 0, width, height);
        _overlayShown = true;
    }

    // Copies size bytes from source through the staging buffer into image, which it leaves ready for the present shader.
    private void UploadToImage(CommandBuffer cmdBuf, void* source, ulong size,
        Silk.NET.Vulkan.Buffer stagingBuffer, DeviceMemory stagingMemory,
        Image image, ref ImageLayout layout, uint rowLength, int width, int height)
    {
        void* mapped;
        Check(_vk.MapMemory(_device, stagingMemory, 0, size, 0, &mapped), "vkMapMemory");
        System.Buffer.MemoryCopy(source, mapped, (long)size, (long)size);
        _vk.UnmapMemory(_device, stagingMemory);

        Check(_vk.ResetCommandBuffer(cmdBuf, 0), "vkResetCommandBuffer");
        BeginCommandBuffer(cmdBuf);

        TransitionImageLayout(cmdBuf, image, layout, ImageLayout.TransferDstOptimal);
        layout = ImageLayout.TransferDstOptimal;

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = rowLength,
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

        _vk.CmdCopyBufferToImage(cmdBuf, stagingBuffer, image, ImageLayout.TransferDstOptimal, 1, &region);

        TransitionImageLayout(cmdBuf, image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
        layout = ImageLayout.ShaderReadOnlyOptimal;

        EndAndSubmitUploadCommands(cmdBuf);
    }

    private void RecordAndSubmit(uint imageIndex, RenderParams renderParams)
    {
        var cmdBuf = _renderCommandBuffer;
        Check(_vk.ResetCommandBuffer(cmdBuf, 0), "vkResetCommandBuffer");
        BeginCommandBuffer(cmdBuf);

        var extent = _swapchain!.Extent;

        ComputeStretchRects(renderParams, extent, out var pushConstants);

        // Srgbフォーマットなら変換は不要。Unorm系は必要
        pushConstants.LinearToSrgb = !_swapchain.SrgbFormat ? 1 : 0;
        pushConstants.IsSourceLinear = renderParams.IsSourceLinear ? 1 : 0;
        pushConstants.OutputBt2020 = _swapchain.IsBt2020 ? 1 : 0;

        // An SDR format holds nothing above 1.0, so FitToDisplay rolls values off into that. A Windows HDR display
        // shows an HDR swapchain's 1.0 at 80 nit, so reference white is written as the SDR white the UI is shown at,
        // and FitToDisplay rolls values off into the display's peak, or the one the editor settings give in its place
        // (RenderParams.DisplayPeakLuminance). Elsewhere an HDR swapchain gets values as they are: on macOS the
        // layer's EDR metadata has Core Animation tone map them (HdrBitmapView), and on Linux the display's headroom
        // is not known.
        PresentLuminance luminance = !_swapchain.IsHdr ? PresentLuminance.Sdr
            : OperatingSystem.IsWindows() ? _windowsDisplay!.Current
            : PresentLuminance.Unknown;
        pushConstants.ReferenceWhite = luminance.ReferenceWhite;

        pushConstants.SetDisplayMapping(PreviewDisplayMapping.For(luminance.Headroom));

        // Colors outside the gamut go the same way: an SDR format holds sRGB, and an HDR swapchain hands colors to a
        // display whose gamut Windows reports (WindowsDisplayLuminance) and HdrBitmapView looks up elsewhere.
        var gamut = !_swapchain.IsHdr ? PreviewGamutMapping.Srgb
            : OperatingSystem.IsWindows() ? _windowsDisplay!.Gamut
            : renderParams.DisplayGamut;
        pushConstants.SetGamut(gamut);

        pushConstants.Overlay = _overlayShown ? 1 : 0;
        pushConstants.ComposeInGamma = _swapchain.IsHdr ? 0 : 1;

        // Transparent around the frame, where a layer that is not opaque shows the UI under it.
        var clearValue = new ClearValue { Color = new ClearColorValue(0f, 0f, 0f, 0f) };
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

        var viewport = new Viewport { X = 0, Y = 0, Width = extent.Width, Height = extent.Height, MinDepth = 0, MaxDepth = 1 };
        var scissor = new Rect2D { Offset = new Offset2D(0, 0), Extent = extent };
        _vk.CmdSetViewport(cmdBuf, 0, 1, &viewport);
        _vk.CmdSetScissor(cmdBuf, 0, 1, &scissor);

        fixed (DescriptorSet* pSet = &_descriptorSet)
        {
            _vk.CmdBindDescriptorSets(cmdBuf, PipelineBindPoint.Graphics, _pipeline.PipelineLayoutHandle, 0, 1, pSet, 0, null);
        }

        _vk.CmdPushConstants(cmdBuf, _pipeline.PipelineLayoutHandle, ShaderStageFlags.FragmentBit, 0, (uint)sizeof(PresentPushConstants), &pushConstants);

        _vk.CmdDraw(cmdBuf, 3, 1, 0, 0);

        _vk.CmdEndRenderPass(cmdBuf);
        Check(_vk.EndCommandBuffer(cmdBuf), "vkEndCommandBuffer");

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

        Check(_vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _inFlightFence), "vkQueueSubmit");
    }

    internal static void ComputeStretchRects(RenderParams p, Extent2D extent, out PresentPushConstants pc)
    {
        pc = default;
        pc.Exposure = p.Exposure;
        pc.TmOperator = (int)p.ToneMapping;
        pc.SampleNearest = p.SampleNearest ? 1 : 0;
        if (p.Checkerboard is { } square)
        {
            pc.CheckerWidth = square.Width;
            pc.CheckerHeight = square.Height;
        }

        if (p.Destination is { } destination)
        {
            pc.DstX = destination.X;
            pc.DstY = destination.Y;
            pc.DstW = destination.Width;
            pc.DstH = destination.Height;
            return;
        }

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

        _running = false;
        _commandQueue.CompleteAdding();
        _presentThread?.Join(2000);

        while (_commandQueue.TryTake(out var cmd))
        {
            if (cmd is RenderCommand.DrawCommand draw)
                draw.BitmapRef.Dispose();
        }

        _commandQueue.Dispose();

        lock (_pendingLock)
        {
            _pendingOverlay?.Dispose();
            _pendingOverlay = null;
        }

        _overlay?.Dispose();
        _overlay = null;

        if (OperatingSystem.IsWindows())
            _windowsDisplay?.Dispose();

        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);

            DestroySourceImage();
            DestroyStagingBuffer();
            DestroyOverlayImage();
            DestroyOverlayStagingBuffer();

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

            if (_commandPool.Handle != 0)
                _vk.DestroyCommandPool(_device, _commandPool, null);

            _vk.DestroyDevice(_device, null);
        }

        FreeOverlayPixels();
    }

    private abstract record RenderCommand
    {
        public sealed record DrawCommand(Ref<Bitmap> BitmapRef, RenderParams Params, long Stamp) : RenderCommand;
        public sealed record ResizeCommand(uint Width, uint Height) : RenderCommand;

        // Only wakes the thread, which takes what is pending (TakePending).
        public sealed record WakeCommand : RenderCommand
        {
            public static readonly WakeCommand Instance = new();
        }
    }

    /// <summary>What the presentation thread does for the commands it took at once.</summary>
    private struct CommandBatch
    {
        // The newest frame to upload, which the batch owns.
        public Ref<Bitmap>? Bitmap;

        // The newest parameters, and the stamp of the request that gave them.
        public NewestRenderParams Newest;

        public (uint Width, uint Height)? Resize;

        // Draw the last frame again, with Newest's parameters when the batch has them.
        public bool Redraw;

        public readonly bool IsEmpty => Bitmap == null && Newest.Params == null && Resize == null && !Redraw;

        public void Add(RenderCommand command)
        {
            switch (command)
            {
                case RenderCommand.DrawCommand draw:
                    Bitmap?.Dispose();
                    Bitmap = draw.BitmapRef;
                    Newest.Take(draw.Params, draw.Stamp);
                    break;

                case RenderCommand.ResizeCommand resize:
                    Resize = (resize.Width, resize.Height);
                    break;
            }
        }
    }

    /// <summary>The newest render parameters taken, by the order in which the view requested them.</summary>
    internal struct NewestRenderParams
    {
        public RenderParams? Params { readonly get; private set; }

        public long Stamp { readonly get; private set; }

        /// <summary>
        /// Returns the parameters to draw with: <paramref name="renderParams"/> unless a later request's parameters were
        /// taken before, in which case those.
        /// </summary>
        public RenderParams Take(RenderParams renderParams, long stamp)
        {
            if (Params == null || stamp >= Stamp)
            {
                Params = renderParams;
                Stamp = stamp;
            }

            return Params.Value;
        }
    }

    // A picture of the UI's overlay and the size of the view it was recorded in.
    private sealed class OverlayContent(SKPicture? picture, float width, float height) : IDisposable
    {
        public SKPicture? Picture { get; } = picture;

        public float Width { get; } = width;

        public float Height { get; } = height;

        public void Dispose() => Picture?.Dispose();
    }
}
