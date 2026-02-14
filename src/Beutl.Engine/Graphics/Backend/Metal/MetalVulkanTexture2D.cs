using System.Runtime.InteropServices;
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

    // Thread-local storage for pending export info allocation (freed after base constructor call)
    [ThreadStatic]
    private static void* s_pendingExportInfoPtr;

    private readonly MetalContext _metalContext;
    private readonly IntPtr _metalTexture;

    public MetalVulkanTexture2D(
        MetalContext metalContext,
        VulkanContext vulkanContext,
        int width,
        int height,
        TextureFormat format,
        ImageUsageFlags usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit |
                               ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit)
        : base(vulkanContext, width, height, format, usage, CreateExportInfo())
    {
        // Free the export info that was allocated in CreateExportInfo
        if (s_pendingExportInfoPtr != null)
        {
            NativeMemory.Free(s_pendingExportInfoPtr);
            s_pendingExportInfoPtr = null;
        }

        _metalContext = metalContext;

        // Export Metal texture from Vulkan image using VK_EXT_metal_objects
        _metalTexture = ExportMetalTexture();

        // Transition to initial layout
        TransitionTo(ImageLayout.ColorAttachmentOptimal);
    }

    private static void* CreateExportInfo()
    {
        var ptr = NativeMemory.Alloc((nuint)sizeof(ExportMetalObjectCreateInfoEXT));
        var exportInfo = (ExportMetalObjectCreateInfoEXT*)ptr;
        exportInfo->SType = StructureType.ExportMetalObjectCreateInfoExt;
        exportInfo->ExportObjectType = ExportMetalObjectTypeFlagsEXT.TextureBitExt;
        exportInfo->PNext = null;
        s_pendingExportInfoPtr = ptr;
        return ptr;
    }

    public override SKSurface CreateSkiaSurface()
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

        return SKSurface.Create(
            _metalContext.SkiaContext,
            backendTexture,
            GRSurfaceOrigin.TopLeft,
            1,
            _format.ToSkiaColorType());
    }

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
