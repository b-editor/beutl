using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe class MetalVulkanSharedTexture : ISharedTexture
{
    private static readonly ILogger s_logger = Log.CreateLogger<MetalVulkanSharedTexture>();
    private readonly MetalContext _metalContext;
    private readonly VulkanContext _vulkanContext;
    private readonly Image _vulkanImage;
    private readonly DeviceMemory _vulkanMemory;
    private readonly ImageView _vulkanImageView;
    private readonly IntPtr _metalTexture;
    private readonly int _width;
    private readonly int _height;
    private readonly TextureFormat _format;
    private readonly ulong _allocationSize;
    private ImageLayout _currentLayout = ImageLayout.Undefined;
    private bool _disposed;

    public MetalVulkanSharedTexture(
        MetalContext metalContext,
        VulkanContext vulkanContext,
        int width,
        int height,
        TextureFormat format)
    {
        _metalContext = metalContext;
        _vulkanContext = vulkanContext;
        _width = width;
        _height = height;
        _format = format;

        var vk = vulkanContext.Vk;
        var device = vulkanContext.Device;

        // Create export info to request Metal texture export
        var exportCreateInfo = new ExportMetalObjectCreateInfoEXT
        {
            SType = StructureType.ExportMetalObjectCreateInfoExt,
            ExportObjectType = ExportMetalObjectTypeFlagsEXT.TextureBitExt
        };

        // Create Vulkan image with Metal export capability
        var imageCreateInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            PNext = &exportCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format.ToVulkanFormat(),
            Extent = new Extent3D((uint)width, (uint)height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit |
                    ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        Image image;
        var result = vk.CreateImage(device, &imageCreateInfo, null, &image);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create Vulkan image: {result}");
        }
        _vulkanImage = image;

        // Get memory requirements
        MemoryRequirements memReqs;
        vk.GetImageMemoryRequirements(device, _vulkanImage, &memReqs);
        _allocationSize = memReqs.Size;

        // Allocate device memory
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = vulkanContext.FindMemoryType(memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };

        DeviceMemory memory;
        result = vk.AllocateMemory(device, &allocInfo, null, &memory);
        if (result != Result.Success)
        {
            vk.DestroyImage(device, _vulkanImage, null);
            throw new InvalidOperationException($"Failed to allocate Vulkan image memory: {result}");
        }
        _vulkanMemory = memory;

        // Bind memory to image
        result = vk.BindImageMemory(device, _vulkanImage, _vulkanMemory, 0);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _vulkanMemory, null);
            vk.DestroyImage(device, _vulkanImage, null);
            throw new InvalidOperationException($"Failed to bind image memory: {result}");
        }

        // Create image view
        var viewInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = _vulkanImage,
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

        ImageView imageView;
        result = vk.CreateImageView(device, &viewInfo, null, &imageView);
        if (result != Result.Success)
        {
            vk.FreeMemory(device, _vulkanMemory, null);
            vk.DestroyImage(device, _vulkanImage, null);
            throw new InvalidOperationException($"Failed to create Vulkan image view: {result}");
        }
        _vulkanImageView = imageView;

        // Export Metal texture from Vulkan image using VK_EXT_metal_objects
        _metalTexture = ExportMetalTexture(vulkanContext);

        // Transition to initial layout
        TransitionTo(ImageLayout.ColorAttachmentOptimal);
    }

    public int Width => _width;

    public int Height => _height;

    public TextureFormat Format => _format;

    public IntPtr VulkanImageHandle => (IntPtr)_vulkanImage.Handle;

    public IntPtr VulkanImageViewHandle => (IntPtr)_vulkanImageView.Handle;

    public void PrepareForRender()
    {
        TransitionTo(ImageLayout.ColorAttachmentOptimal);
    }

    public void PrepareForSampling()
    {
        TransitionTo(ImageLayout.ShaderReadOnlyOptimal);
    }

    private void TransitionTo(ImageLayout layout)
    {
        if (_currentLayout == layout)
            return;

        _vulkanContext.TransitionImageLayout(_vulkanImage, _currentLayout, layout);
        _currentLayout = layout;
    }

    private IntPtr ExportMetalTexture(VulkanContext vulkanContext)
    {
        var vk = vulkanContext.Vk;

        if (!vk.TryGetDeviceExtension<ExtMetalObjects>(vulkanContext.Instance, vulkanContext.Device, out var metalObjects))
        {
            s_logger.LogWarning("VK_EXT_metal_objects extension not available");
            return IntPtr.Zero;
        }

        // Export Metal texture info
        var exportTextureInfo = new ExportMetalTextureInfoEXT
        {
            SType = StructureType.ExportMetalTextureInfoExt,
            Image = _vulkanImage,
            ImageView = _vulkanImageView,
            BufferView = default,
            Plane = ImageAspectFlags.ColorBit
        };

        var exportInfo = new ExportMetalObjectsInfoEXT
        {
            SType = StructureType.ExportMetalObjectsInfoExt,
            PNext = &exportTextureInfo
        };

        metalObjects.ExportMetalObjects(vulkanContext.Device, &exportInfo);

        return exportTextureInfo.MtlTexture;
    }

    public SKSurface CreateSkiaSurface()
    {
        if (_metalTexture == IntPtr.Zero)
        {
            s_logger.LogWarning("Cannot create SkiaSurface: Metal texture handle is null");
            throw new InvalidOperationException("Metal texture handle is null");
        }

        // Use the exported Metal texture for SkiaSharp rendering
        var textureInfo = new GRMtlTextureInfo(_metalTexture);
        var backendTexture = new GRBackendTexture(_width, _height, false, textureInfo);

        return SKSurface.Create(
            _metalContext.SkiaContext,
            backendTexture,
            GRSurfaceOrigin.TopLeft,
            1,
            _format.ToSkiaColorType());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _vulkanContext.Vk;
        var device = _vulkanContext.Device;

        if (_vulkanImageView.Handle != 0)
        {
            vk.DestroyImageView(device, _vulkanImageView, null);
        }

        if (_vulkanImage.Handle != 0)
        {
            vk.DestroyImage(device, _vulkanImage, null);
        }

        if (_vulkanMemory.Handle != 0)
        {
            vk.FreeMemory(device, _vulkanMemory, null);
        }
    }
}
