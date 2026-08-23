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

        // Get memory requirements
        MemoryRequirements memReqs;
        vk.GetImageMemoryRequirements(device, _image, &memReqs);
        _allocationSize = memReqs.Size;

        // Allocate memory
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = context.FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        DeviceMemory memory;
        result = vk.AllocateMemory(device, &allocInfo, null, &memory);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to allocate Vulkan image memory: {result}");
        }

        _memory = memory;

        // Bind memory to image
        result = vk.BindImageMemory(device, _image, _memory, 0);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
            throw new InvalidOperationException($"Failed to bind image memory: {result}");
        }

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = format.ToVulkanFormat(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = format.GetAspectMask(),
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        ImageView imageView;
        result = vk.CreateImageView(device, &viewInfo, null, &imageView);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _memory, null);
            vk.DestroyImage(device, _image, null);
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

        // Create staging buffer
        using var stagingBuffer = new VulkanBuffer(
            _context,
            (ulong)data.Length,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Copy data to staging buffer
        stagingBuffer.Upload(data);

        // Transition to transfer destination
        TransitionTo(ImageLayout.TransferDstOptimal);

        // Copy buffer to image
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
            _context.Vk.CmdCopyBufferToImage(
                cmd, stagingBuffer.Handle, _image, ImageLayout.TransferDstOptimal, 1, &region);
        });

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

        var vkImageInfo = new GRVkImageInfo
        {
            Image = _image.Handle,
            Alloc = new GRVkAlloc { Memory = (ulong)_memory.Handle, Offset = 0, Size = _allocationSize },
            ImageTiling = (uint)ImageTiling.Optimal,
            // The layout the image is actually in, not the one it is usually in. Skia takes this as the
            // starting point for its own tracking and barriers, so declaring a layout the image has not
            // reached tells it to skip a transition it needs - and, when the image was just cleared, to
            // treat contents as undefined that are not.
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

        using var backendRenderTarget = new GRBackendRenderTarget(_width, _height, vkImageInfo);

        var grContext = _context.SkiaContext;
        var surface = SKSurface.Create(grContext, backendRenderTarget, GRSurfaceOrigin.TopLeft,
            _format.ToSkiaColorType(), SKColorSpace.CreateSrgbLinear());

        if (surface == null)
        {
            throw new InvalidOperationException("Failed to create SkiaSharp surface from Vulkan backend render target");
        }

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
        bool requiresSubmission = _currentLayout != ImageLayout.ColorAttachmentOptimal
            || RequiresVulkanToSkiaHandoff;
        TransitionTo(ImageLayout.ColorAttachmentOptimal);
        if (requiresSubmission)
        {
            _context.FlushCommands(waitForCompletion: false);
        }
        MarkSkiaAccess();
        _hasTransparentContents = false;
    }

    public virtual void PrepareForSkiaSampling(bool requireCompletion)
    {
        if (RequiresVulkanToSkiaHandoff)
        {
            _context.FlushCommands(requireCompletion);
        }
        MarkSkiaAccess();
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

    public void TransitionTo(ImageLayout layout)
    {
        if (_currentLayout == layout)
        {
            _accessDomain = TextureAccessDomain.Vulkan;
            return;
        }

        _context.TransitionImageLayout(_image, _currentLayout, layout, _format.GetAspectMask());
        _currentLayout = layout;
        _accessDomain = TextureAccessDomain.Vulkan;
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
