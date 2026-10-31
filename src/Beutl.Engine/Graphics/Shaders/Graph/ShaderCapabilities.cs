using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Shaders;

[Flags]
public enum ImageUsage
{
    Sampled = 1, Storage = 2, ColorAttachment = 4, DepthStencilAttachment = 8,
    TransferSource = 16, TransferDestination = 32,
}

/// <summary>The features and limits enabled on the rendering device.</summary>
/// <remarks>Indirect command contents are authored data. Counts, indices and dispatch group counts must
/// fit these limits and their bound buffers. Nonzero indirect firstInstance requires IndirectFirstInstance.</remarks>
public sealed class ShaderCapabilities
{
    private readonly VulkanContext _context;
    private readonly PhysicalDeviceLimits _limits;
    private readonly PhysicalDeviceFeatures _features;

    private ShaderCapabilities(VulkanContext context)
    {
        _context = context;
        _limits = context.Limits;
        _features = context.EnabledFeatures;
    }

    /// <summary>Queries the supplied rendering context; call on its owning render thread.</summary>
    public static ShaderCapabilities Query(IGraphicsContext context)
        => new(GLSLFilterPipeline.TryResolveVulkan(context)
            ?? throw new NotSupportedException("Shader graphs require a Vulkan rendering context."));

    public bool GeometryShaders => _features.GeometryShader;
    public bool TessellationShaders => _features.TessellationShader;
    public bool CubeArrays => _features.ImageCubeArray;
    public bool MultipleIndirectDraws => _features.MultiDrawIndirect;
    public bool IndirectFirstInstance => _features.DrawIndirectFirstInstance;
    public bool ShaderInt64 => _features.ShaderInt64;
    public bool ShaderFloat64 => _features.ShaderFloat64;
    public bool VertexStorageWrites => _features.VertexPipelineStoresAndAtomics;
    public bool FragmentStorageWrites => _features.FragmentStoresAndAtomics;
    public uint MaxImageDimension2D => _limits.MaxImageDimension2D;
    public uint MaxImageDimension3D => _limits.MaxImageDimension3D;
    public bool TriangleFans => _context.SupportsTriangleFans;
    public uint VertexStrideAlignment => _context.VertexStrideAlignment;
    public uint MaxVertexBindings => _limits.MaxVertexInputBindings;
    public uint MaxVertexAttributes => _limits.MaxVertexInputAttributes;
    public uint MaxVertexStride => _limits.MaxVertexInputBindingStride;
    public uint MaxVertexAttributeOffset => _limits.MaxVertexInputAttributeOffset;
    public uint MaxFramebufferWidth => _limits.MaxFramebufferWidth;
    public uint MaxFramebufferHeight => _limits.MaxFramebufferHeight;
    public uint MaxPushConstantBytes => VulkanPipeline3D.MaxPushConstantsSize;
    public uint MaxGeometryInvocations => _limits.MaxGeometryShaderInvocations;
    public uint MaxGeometryOutputVertices => _limits.MaxGeometryOutputVertices;
    public uint MaxImageArrayLayers => _limits.MaxImageArrayLayers;
    public uint MaxColorAttachments => _limits.MaxColorAttachments;
    public uint MaxIndirectDrawCount => _limits.MaxDrawIndirectCount;
    public uint MaxPatchControlPoints => _limits.MaxTessellationPatchSize;
    public uint MaxStorageBufferBytes => _limits.MaxStorageBufferRange;
    public uint MaxUniformBufferBytes => _limits.MaxUniformBufferRange;
    public uint MaxComputeInvocations => _limits.MaxComputeWorkGroupInvocations;
    public uint MaxComputeSharedMemoryBytes => _limits.MaxComputeSharedMemorySize;
    public unsafe (uint X, uint Y, uint Z) MaxComputeGroupCount
    {
        get { var limits = _limits; return (limits.MaxComputeWorkGroupCount[0], limits.MaxComputeWorkGroupCount[1], limits.MaxComputeWorkGroupCount[2]); }
    }
    public unsafe (uint X, uint Y, uint Z) MaxComputeGroupSize
    {
        get { var limits = _limits; return (limits.MaxComputeWorkGroupSize[0], limits.MaxComputeWorkGroupSize[1], limits.MaxComputeWorkGroupSize[2]); }
    }

    /// <summary>Checks the exact image shape, format and combination of uses before graph recording.</summary>
    public bool SupportsImage(ImageDescription description, ImageUsage usage)
    {
        description.Validate();
        if (description.Kind == ImageKind.CubeArray && !CubeArrays) return false;
        ImageUsageFlags flags = ToNative(usage);
        try { ShaderGraphImage.Validate(_context, description, flags); return true; }
        catch (NotSupportedException) { return false; }
    }

    internal static ImageUsageFlags ToNative(ImageUsage usage)
    {
        if (usage == 0 || (usage & ~(ImageUsage.Sampled | ImageUsage.Storage | ImageUsage.ColorAttachment
                | ImageUsage.DepthStencilAttachment | ImageUsage.TransferSource | ImageUsage.TransferDestination)) != 0)
            throw new ArgumentOutOfRangeException(nameof(usage));
        ImageUsageFlags flags = 0;
        if ((usage & ImageUsage.Sampled) != 0) flags |= ImageUsageFlags.SampledBit;
        if ((usage & ImageUsage.Storage) != 0) flags |= ImageUsageFlags.StorageBit;
        if ((usage & ImageUsage.ColorAttachment) != 0) flags |= ImageUsageFlags.ColorAttachmentBit;
        if ((usage & ImageUsage.DepthStencilAttachment) != 0) flags |= ImageUsageFlags.DepthStencilAttachmentBit;
        if ((usage & ImageUsage.TransferSource) != 0) flags |= ImageUsageFlags.TransferSrcBit;
        if ((usage & ImageUsage.TransferDestination) != 0) flags |= ImageUsageFlags.TransferDstBit;
        return flags;
    }
}
