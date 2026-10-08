using Beutl.Graphics.Backend.Vulkan;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Metal;

internal sealed unsafe class MetalVulkanTexture2D : VulkanTexture2D
{
    private static readonly ILogger s_logger = Log.CreateLogger<MetalVulkanTexture2D>();

    private readonly MetalContext _metalContext;
    private readonly MetalVulkanTimeline? _timeline;
    private readonly IntPtr _metalTexture;

    private MetalVulkanTexture2D(
        MetalContext metalContext,
        MetalVulkanTimeline? timeline,
        VulkanContext vulkanContext,
        int width,
        int height,
        TextureFormat format,
        ImageUsageFlags usage,
        ExportMetalObjectCreateInfoEXT* exportInfo)
        : base(vulkanContext, width, height, format, usage, exportInfo)
    {
        _metalContext = metalContext;
        _timeline = timeline;

        // Export Metal texture from Vulkan image using VK_EXT_metal_objects
        _metalTexture = ExportMetalTexture();

        // Transition to initial layout
        TransitionTo(SkiaInteropLayout);
    }

    public static MetalVulkanTexture2D Create(
        MetalContext metalContext,
        MetalVulkanTimeline? timeline,
        VulkanContext vulkanContext,
        int width,
        int height,
        TextureFormat format,
        ImageUsageFlags usage = VulkanTexture2D.ColorTextureUsage)
    {
        // Only vkCreateImage reads the export request, inside the base constructor, so it lives on this frame
        // and nothing is left to free when construction throws.
        var exportInfo = new ExportMetalObjectCreateInfoEXT
        {
            SType = StructureType.ExportMetalObjectCreateInfoExt,
            ExportObjectType = ExportMetalObjectTypeFlagsEXT.TextureBitExt,
            PNext = null,
        };
        return new MetalVulkanTexture2D(metalContext, timeline, vulkanContext, width, height, format, usage, &exportInfo);
    }

    public override SKSurface CreateSkiaSurface(SKColorSpace colorSpace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_metalTexture == IntPtr.Zero)
        {
            s_logger.LogWarning("Cannot create SkiaSurface: Metal texture handle is null");
            throw new InvalidOperationException("Metal texture handle is null");
        }

        // Use the exported Metal texture for SkiaSharp rendering
        var textureInfo = new GRMtlTextureInfo(_metalTexture);
        var backendTexture = new GRBackendTexture(_width, _height, false, textureInfo);

        SKSurface surface = SKSurface.Create(
            _metalContext.SkiaContext,
            backendTexture,
            GRSurfaceOrigin.TopLeft,
            1,
            _format.ToSkiaColorType(),
            colorSpace);
        return surface;
    }

    protected override void SubmitForSkia(bool requireCompletion)
    {
        // MoltenVK and Skia's Metal queue share no submission order. Without a shared timeline, or for a CPU
        // reader, the backend work has to finish before Skia touches the texture.
        if (_timeline is null || requireCompletion)
        {
            _context.FlushCommands(waitForCompletion: true);
            return;
        }

        _timeline.OrderVulkanWorkBeforeSkia();
    }

    // A Vulkan pass that reads what Skia drew waits for Skia on the CPU; see MetalVulkanTimeline for why.
    internal override bool OrdersSkiaWritesOnGpu => false;

    private IntPtr ExportMetalTexture()
    {
        var vk = _context.Vk;

        if (!vk.TryGetDeviceExtension<ExtMetalObjects>(_context.Instance, _context.Device, out var metalObjects))
        {
            s_logger.LogWarning("VK_EXT_metal_objects extension not available");
            return IntPtr.Zero;
        }

        // Export Metal texture info
        var exportTextureInfo = new ExportMetalTextureInfoEXT
        {
            SType = StructureType.ExportMetalTextureInfoExt,
            Image = _image,
            ImageView = _imageView,
            BufferView = default,
            Plane = ImageAspectFlags.ColorBit
        };

        var exportInfo = new ExportMetalObjectsInfoEXT
        {
            SType = StructureType.ExportMetalObjectsInfoExt,
            PNext = &exportTextureInfo
        };

        metalObjects.ExportMetalObjects(_context.Device, &exportInfo);

        return exportTextureInfo.MtlTexture;
    }
}
