using System;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="ITexture2D"/>.
/// </summary>
internal unsafe class VulkanTexture2D : ITexture2D, ITransparentClearableTexture, IVulkanContextResource
{
    protected readonly VulkanContext _context;
    protected readonly Silk.NET.Vulkan.Image _image;
    protected readonly DeviceMemory _memory;
    protected readonly ImageView _imageView;
    protected readonly int _width;
    protected readonly int _height;
    protected readonly TextureFormat _format;
    protected readonly ulong _allocationSize;

    public VulkanContext OwnerContext => _context;
    protected ImageLayout _currentLayout = ImageLayout.Undefined;
    private TextureAccessDomain _accessDomain;
    private GRBackendRenderTarget? _skiaBackendRenderTarget;
    private bool _hasTransparentContents;
    protected bool _disposed;

    public VulkanTexture2D(
        VulkanContext context,
        int width,
        int height,
        TextureFormat format,
        ImageUsageFlags usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit)
        : this(context, width, height, format, usage, null)
    {
    }

    protected VulkanTexture2D(
        VulkanContext context,
        int width,
        int height,
        TextureFormat format,
        ImageUsageFlags usage,
        void* pNext)
    {
        _context = context;
        _width = width;
        _height = height;
        _format = format;

        var vk = context.Vk;
        var device = context.Device;

        // Create image
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = pNext,
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        Silk.NET.Vulkan.Image image;
        var result = vk.CreateImage(device, &imageInfo, null, &image);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create Vulkan image: {result}");
        }

        _image = image;

        _memory = context.AllocateAndBindImageMemory(_image, "image", out ulong allocationSize);
        _allocationSize = allocationSize;

        result = context.TryCreateSingleLayerView(_image, format, arrayLayer: 0, out ImageView imageView);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, _image, null);
            vk.FreeMemory(device, _memory, null);
            throw new InvalidOperationException($"Failed to create Vulkan image view: {result}");
        }

        _imageView = imageView;
    }

    public int Width => _width;

    public int Height => _height;

    public TextureFormat Format => _format;

    public IntPtr NativeHandle => (IntPtr)_image.Handle;

    public IntPtr NativeViewHandle => (IntPtr)_imageView.Handle;

    public Silk.NET.Vulkan.Image ImageHandle => _image;

    public ImageView ImageViewHandle => _imageView;

    public void Upload(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Transition to transfer destination
        TransitionTo(ImageLayout.TransferDstOptimal);

        _context.UploadToImageLayer(
            data,
            _image,
            ImageAspectFlags.ColorBit,
            destinationArrayLayer: 0,
            (uint)_width,
            (uint)_height);

        // Transition to shader read
        TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
        _hasTransparentContents = false;
    }

    public byte[] DownloadPixels()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Transition to transfer source
        TransitionTo(ImageLayout.TransferSrcOptimal);

        int bytesPerPixel = _format switch
        {
            TextureFormat.RGBA8Unorm or TextureFormat.BGRA8Unorm => 4,
            TextureFormat.RGBA16Float => 8,
            TextureFormat.RGBA32Float => 16,
            TextureFormat.R8Unorm => 1,
            TextureFormat.R16Float => 2,
            TextureFormat.R32Float => 4,
            _ => 4
        };

        ulong bufferSize = (ulong)(_width * _height * bytesPerPixel);
        var pixelData = new byte[bufferSize];

        // Create staging buffer
        using var stagingBuffer = new VulkanBuffer(
            _context,
            bufferSize,
            BufferUsage.TransferDestination,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Copy image to buffer
        _context.RecordCommands(cmd =>
        {
            var region = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = 0,
                BufferImageHeight = 0,
                ImageSubresource = new ImageSubresourceLayers
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    MipLevel = 0,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                },
                ImageOffset = new Offset3D(0, 0, 0),
                ImageExtent = new Extent3D((uint)_width, (uint)_height, 1)
            };

            // ReSharper disable once AccessToDisposedClosure
            _context.Vk.CmdCopyImageToBuffer(
                cmd, _image, ImageLayout.TransferSrcOptimal, stagingBuffer.Handle, 1, &region);
        });

        // Restore the sampled layout in the same batch, then wait before mapping the staging buffer.
        TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
        _context.FlushCommands(waitForCompletion: true);

        // Read data from staging buffer
        var srcPtr = stagingBuffer.Map();
        Marshal.Copy(srcPtr, pixelData, 0, (int)bufferSize);
        stagingBuffer.Unmap();

        return pixelData;
    }

    /// <summary>
    /// The layout established before handing backend writes to Skia for drawing.
    /// </summary>
    internal const ImageLayout SkiaInteropLayout = ImageLayout.ColorAttachmentOptimal;

    /// <summary>
    /// Builds the description Skia is handed for this image.
    /// </summary>
    /// <remarks>
    /// Subsequent backend transitions update the same mutable state Skia holds. In particular the
    /// allocation clear must replace Undefined before the first Skia access, otherwise Skia is allowed
    /// to discard that clear. The retained backend render target also observes transitions made by Skia.
    /// </remarks>
    internal GRVkImageInfo CreateSkiaImageInfo() => new()
    {
        Image = _image.Handle,
        Alloc = new GRVkAlloc { Memory = (ulong)_memory.Handle, Offset = 0, Size = _allocationSize },
        ImageTiling = (uint)ImageTiling.Optimal,
        ImageLayout = (uint)_currentLayout,
        Format = (uint)_format.ToVulkanFormat(),
        ImageUsageFlags = (uint)(ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit |
                                 ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit),
        SampleCount = 1,
        LevelCount = 1,
        CurrentQueueFamily = _context.GraphicsQueueFamilyIndex,
        Protected = false,
        SharingMode = (uint)SharingMode.Exclusive
    };

    public virtual SKSurface CreateSkiaSurface()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // On macOS, use raster surface (Metal interop handles rendering separately)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var info = new SKImageInfo(_width, _height, _format.ToSkiaColorType(), SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
            MarkSkiaAccess();
            return SKSurface.Create(info);
        }

        // Copies made by Skia share this handle's mutable Vulkan state. Retain one handle for the
        // texture's lifetime, including when Renderer3D creates a new surface for each frame.
        _skiaBackendRenderTarget ??= new GRBackendRenderTarget(_width, _height, CreateSkiaImageInfo());

        var grContext = _context.SkiaContext;
        var surface = SKSurface.Create(grContext, _skiaBackendRenderTarget, GRSurfaceOrigin.TopLeft,
            _format.ToSkiaColorType(), SKColorSpace.CreateSrgbLinear());

        if (surface == null)
        {
            throw new InvalidOperationException("Failed to create SkiaSharp surface from Vulkan backend render target");
        }

        // Wrapping records no Skia access and does not submit a pending backend upload. Preserve
        // Vulkan ownership until PrepareForSkiaRendering/Sampling performs that first handoff.
        if (!RequiresVulkanToSkiaHandoff)
            MarkSkiaAccess();
        return surface;
    }

    public void PrepareForRender()
    {
        TransitionTo(ImageLayout.ColorAttachmentOptimal);
        _accessDomain = TextureAccessDomain.Vulkan;
        _hasTransparentContents = false;
    }

    public void PrepareForSampling()
    {
        TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
        _accessDomain = TextureAccessDomain.Vulkan;
    }

    public bool RequiresSkiaFlushForBackendInterop => _accessDomain == TextureAccessDomain.Skia;

    protected bool RequiresVulkanToSkiaHandoff => _accessDomain == TextureAccessDomain.Vulkan;

    public virtual void PrepareForSkiaRendering()
    {
        // Consecutive Skia draws need neither a backend barrier nor a flush of Skia's task graph.
        if (_skiaBackendRenderTarget != null && _accessDomain == TextureAccessDomain.Skia)
        {
            _hasTransparentContents = false;
            return;
        }

        bool requiresSubmission = _currentLayout != SkiaInteropLayout
            || RequiresVulkanToSkiaHandoff;
        TransitionTo(SkiaInteropLayout);
        if (requiresSubmission)
        {
            SubmitForSkia(requireCompletion: false);
        }
        MarkSkiaAccess();
        _hasTransparentContents = false;
    }

    public virtual void PrepareForSkiaSampling(bool requireCompletion)
    {
        if (RequiresVulkanToSkiaHandoff)
        {
            SubmitForSkia(requireCompletion);
        }
        MarkSkiaAccess();
    }

    /// <summary>Submits the backend work Skia is about to consume.</summary>
    /// <remarks>
    /// Skia shares this texture's Vulkan queue, so submission order carries the hand-off; only a CPU reader
    /// needs the work finished.
    /// </remarks>
    protected virtual void SubmitForSkia(bool requireCompletion)
    {
        _context.FlushCommands(requireCompletion);
    }

    /// <summary>Gets whether a backend pass can read what Skia drew here without the CPU waiting for Skia.</summary>
    internal virtual bool OrdersSkiaWritesOnGpu => false;

    /// <summary>Holds the next backend batch until Skia's submitted writes to this texture finish.</summary>
    /// <remarks>Call only when <see cref="OrdersSkiaWritesOnGpu"/> is <see langword="true"/>, after Skia submitted.</remarks>
    internal virtual void OrderSkiaWritesBeforeBackend()
    {
        throw new NotSupportedException("This texture cannot order Skia's writes on the GPU.");
    }

    protected void MarkSkiaAccess()
    {
        _accessDomain = TextureAccessDomain.Skia;
    }

    bool ITransparentClearableTexture.HasTransparentContents => _hasTransparentContents;

    void ITransparentClearableTexture.ClearToTransparent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_format.IsDepthFormat())
        {
            throw new InvalidOperationException(
                "A depth texture cannot be initialized with a transparent color clear.");
        }
        if (_hasTransparentContents)
            return;

        TransitionTo(ImageLayout.TransferDstOptimal);
        _context.RecordCommands(commandBuffer =>
        {
            var range = new ImageSubresourceRange
            {
                AspectMask = _format.GetAspectMask(),
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            };
            var transparent = new ClearColorValue(0, 0, 0, 0);
            _context.Vk.CmdClearColorImage(
                commandBuffer,
                _image,
                ImageLayout.TransferDstOptimal,
                &transparent,
                1,
                &range);
        });
        _accessDomain = TextureAccessDomain.Vulkan;
        _hasTransparentContents = true;
    }

    internal void MarkContentsUnknown()
    {
        _hasTransparentContents = false;
    }

    void ITransparentClearableTexture.MarkContentsTransparent()
    {
        _hasTransparentContents = true;
    }

    public void TransitionTo(ImageLayout layout)
    {
        if (_skiaBackendRenderTarget != null && _accessDomain == TextureAccessDomain.Skia)
        {
            // Snapshot and readback can change the source layout too. Flush before reading its
            // state, then enqueue our commands after Skia's submission on the shared Vulkan queue.
            _context.SkiaContext.Flush(submit: true, synchronous: false);
            _currentLayout = SkiaVulkanInterop.GetImageLayout(_skiaBackendRenderTarget);
        }

        if (_currentLayout == layout)
        {
            _accessDomain = TextureAccessDomain.Vulkan;
            return;
        }

        _context.TransitionImageLayout(_image, _currentLayout, layout, _format.GetAspectMask());
        _currentLayout = layout;
        if (_skiaBackendRenderTarget != null)
            SkiaVulkanInterop.SetImageLayout(_skiaBackendRenderTarget, layout);
        _accessDomain = TextureAccessDomain.Vulkan;
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _skiaBackendRenderTarget?.Dispose();
        _skiaBackendRenderTarget = null;

        ImageView imageView = _imageView;
        Silk.NET.Vulkan.Image image = _image;
        DeviceMemory memory = _memory;
        _context.DeferRelease(() =>
        {
            var vk = _context.Vk;
            var device = _context.Device;

            if (imageView.Handle != 0)
            {
                vk.DestroyImageView(device, imageView, null);
            }

            if (image.Handle != 0)
            {
                vk.DestroyImage(device, image, null);
            }

            if (memory.Handle != 0)
            {
                vk.FreeMemory(device, memory, null);
            }
        });
    }
}

internal enum TextureAccessDomain : byte
{
    None,
    Skia,
    Vulkan,
}
