using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe class VulkanSharedTexture : ISharedTexture
{
    private readonly VulkanContext _context;
    private readonly Image _image;
    private readonly DeviceMemory _memory;
    private readonly ImageView _imageView;
    private readonly int _width;
    private readonly int _height;
    private readonly TextureFormat _format;
    private readonly ulong _allocationSize;
    private ImageLayout _currentLayout = ImageLayout.Undefined;
    private bool _disposed;

    public VulkanSharedTexture(VulkanContext context, int width, int height, TextureFormat format)
    {
        _context = context;
        _width = width;
        _height = height;
        _format = format;

        var vk = context.Vk;

        // Create image with color attachment and transfer src for blitting
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D { Width = (uint)width, Height = (uint)height, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit |
                    ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        fixed (Image* pImage = &_image)
        {
            var result = vk.CreateImage(context.Device, &imageInfo, null, pImage);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to create Vulkan image: {result}");
        }

        // Get memory requirements
        MemoryRequirements memReqs;
        vk.GetImageMemoryRequirements(context.Device, _image, &memReqs);
        _allocationSize = memReqs.Size;

        // Allocate device local memory
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = context.FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        fixed (DeviceMemory* pMemory = &_memory)
        {
            var result = vk.AllocateMemory(context.Device, &allocInfo, null, pMemory);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to allocate Vulkan image memory: {result}");
        }

        // Bind memory to image
        vk.BindImageMemory(context.Device, _image, _memory, 0);

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _image,
            ViewType = ImageViewType.Type2D,
            Format = format.ToVulkanFormat(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        fixed (ImageView* pView = &_imageView)
        {
            var result = vk.CreateImageView(context.Device, &viewInfo, null, pView);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to create Vulkan image view: {result}");
        }

        TransitionTo(ImageLayout.ColorAttachmentOptimal);
    }

    public int Width => _width;

    public int Height => _height;

    public TextureFormat Format => _format;

    public IntPtr VulkanImageHandle => (IntPtr)_image.Handle;

    public IntPtr VulkanImageViewHandle => (IntPtr)_imageView.Handle;

    public void PrepareForRender()
    {
        TransitionTo(ImageLayout.ColorAttachmentOptimal);
    }

    public void PrepareForSampling()
    {
        TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    public SKSurface CreateSkiaSurface()
    {
        // On macOS, use raster surface (Metal interop handles rendering separately)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var info = new SKImageInfo(_width, _height, _format.ToSkiaColorType(), SKAlphaType.Premul);
            return SKSurface.Create(info);
        }

        var vkImageInfo = new GRVkImageInfo
        {
            Image = _image.Handle,
            Alloc = new GRVkAlloc { Memory = (ulong)_memory.Handle, Offset = 0, Size = _allocationSize },
            ImageTiling = (uint)ImageTiling.Optimal,
            ImageLayout = (uint)ImageLayout.ColorAttachmentOptimal,
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
            _format.ToSkiaColorType());

        if (surface == null)
        {
            throw new InvalidOperationException("Failed to create SkiaSharp surface from Vulkan backend render target");
        }

        return surface;
    }


    public unsafe byte[] DownloadPixels()
    {
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
        _context.SubmitImmediateCommands(cmd =>
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

            _context.Vk.CmdCopyImageToBuffer(cmd, _image, ImageLayout.TransferSrcOptimal, stagingBuffer.Handle, 1, &region);
        });

        // Read data from staging buffer
        var srcPtr = stagingBuffer.Map();
        System.Runtime.InteropServices.Marshal.Copy(srcPtr, pixelData, 0, (int)bufferSize);
        stagingBuffer.Unmap();

        // Transition back to color attachment
        TransitionTo(ImageLayout.ColorAttachmentOptimal);

        return pixelData;
    }

    private void TransitionTo(ImageLayout layout)
    {
        if (_currentLayout == layout)
            return;

        _context.TransitionImageLayout(_image, _currentLayout, layout);
        _currentLayout = layout;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _context.Vk.DestroyImageView(_context.Device, _imageView, null);
        _context.Vk.DestroyImage(_context.Device, _image, null);
        _context.Vk.FreeMemory(_context.Device, _memory, null);
    }
}
