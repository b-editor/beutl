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
    private readonly IntPtr _metalTexture;
    private readonly int _width;
    private readonly int _height;
    private readonly TextureFormat _format;
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
            Format = GetVulkanFormat(format),
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

        // Allocate device memory
        var allocInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memReqs.Size,
            MemoryTypeIndex = FindMemoryType(vulkanContext, memReqs.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
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

        // Export Metal texture from Vulkan image using VK_EXT_metal_objects
        _metalTexture = ExportMetalTexture(vulkanContext);
    }

    public int Width => _width;

    public int Height => _height;

    public TextureFormat Format => _format;

    public IntPtr VulkanImageHandle => (IntPtr)_vulkanImage.Handle;

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
            ImageView = default, // We're exporting the image directly
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

    private static uint FindMemoryType(VulkanContext context, uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProps;
        context.Vk.GetPhysicalDeviceMemoryProperties(context.PhysicalDevice, &memProps);

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

    private static Format GetVulkanFormat(TextureFormat textureFormat)
    {
        return textureFormat switch
        {
            TextureFormat.RGBA8Unorm => Silk.NET.Vulkan.Format.R8G8B8A8Unorm,
            TextureFormat.BGRA8Unorm => Silk.NET.Vulkan.Format.B8G8R8A8Unorm,
            TextureFormat.RGBA16Float => Silk.NET.Vulkan.Format.R16G16B16A16Sfloat,
            TextureFormat.RGBA32Float => Silk.NET.Vulkan.Format.R32G32B32A32Sfloat,
            TextureFormat.R8Unorm => Silk.NET.Vulkan.Format.R8Unorm,
            TextureFormat.R16Float => Silk.NET.Vulkan.Format.R16Sfloat,
            TextureFormat.R32Float => Silk.NET.Vulkan.Format.R32Sfloat,
            _ => Silk.NET.Vulkan.Format.R8G8B8A8Unorm
        };
    }

    public SKSurface? CreateSkiaSurface()
    {
        if (_metalTexture == IntPtr.Zero)
        {
            s_logger.LogWarning("Cannot create SkiaSurface: Metal texture handle is null");
            return null;
        }

        // Use the exported Metal texture for SkiaSharp rendering
        var textureInfo = new GRMtlTextureInfo(_metalTexture);
        var backendTexture = new GRBackendTexture(_width, _height, false, textureInfo);

        return SKSurface.Create(
            _metalContext.SkiaContext,
            backendTexture,
            GRSurfaceOrigin.TopLeft,
            1,
            GetSkiaColorType(_format));
    }

    private static SKColorType GetSkiaColorType(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8Unorm => SKColorType.Rgba8888,
            TextureFormat.BGRA8Unorm => SKColorType.Bgra8888,
            TextureFormat.RGBA16Float => SKColorType.RgbaF16,
            TextureFormat.RGBA32Float => SKColorType.RgbaF32,
            TextureFormat.R8Unorm => SKColorType.Gray8,
            TextureFormat.R16Float => SKColorType.AlphaF16,
            TextureFormat.R32Float => SKColorType.RgbaF32,
            _ => SKColorType.Rgba8888
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _vulkanContext.Vk;
        var device = _vulkanContext.Device;

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
