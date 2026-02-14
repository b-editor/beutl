using System.Runtime.InteropServices;
using Beutl.Graphics.Backend.Metal;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics3D;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Composite;

internal sealed class CompositeContext : IGraphicsContext
{
    private bool _disposed;

    public CompositeContext(VulkanInstance vulkanInstance, VulkanPhysicalDeviceInfo physicalDevice)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("CompositeContext is only available on macOS");
        }

        // MoltenVKの場合はMetalコンテキストを使用する
        if (physicalDevice.IsMoltenVK)
        {
            Metal = new MetalContext();
        }

        Vulkan = new VulkanContext(vulkanInstance, physicalDevice);
    }

    public GraphicsBackend Backend => GraphicsBackend.Metal;

    public GRContext SkiaContext => Metal?.SkiaContext ?? Vulkan.SkiaContext!;

    public MetalContext? Metal { get; }

    public VulkanContext Vulkan { get; }

    public bool Supports3DRendering => Vulkan.Supports3DRendering;

    public ITexture2D CreateTexture2D(int width, int height, TextureFormat format)
    {
        if (Metal != null && !format.IsDepthFormat())
        {
            return new MetalVulkanTexture2D(Metal, Vulkan, width, height, format);
        }

        return Vulkan.CreateTexture2D(width, height, format);
    }

    public ITextureCube CreateTextureCube(int size, TextureFormat format)
    {
        return Vulkan.CreateTextureCube(size, format);
    }

    public ITextureArray CreateTextureArray(int width, int height, uint arraySize, TextureFormat format)
    {
        return Vulkan.CreateTextureArray(width, height, arraySize, format);
    }

    public ITextureCubeArray CreateTextureCubeArray(int size, uint arraySize, TextureFormat format)
    {
        return Vulkan.CreateTextureCubeArray(size, arraySize, format);
    }

    public IBuffer CreateBuffer(ulong size, BufferUsage usage, MemoryProperty memoryProperty)
    {
        return Vulkan.CreateBuffer(size, usage, memoryProperty);
    }

    public IShaderCompiler CreateShaderCompiler()
    {
        return Vulkan.CreateShaderCompiler();
    }

    public IRenderPass3D CreateRenderPass3D(
        IReadOnlyList<TextureFormat> colorFormats,
        TextureFormat depthFormat = TextureFormat.Depth32Float,
        AttachmentLoadOp colorLoadOp = AttachmentLoadOp.Clear,
        AttachmentLoadOp depthLoadOp = AttachmentLoadOp.Clear)
    {
        return Vulkan.CreateRenderPass3D(colorFormats, depthFormat, colorLoadOp, depthLoadOp);
    }

    public IFramebuffer3D CreateFramebuffer3D(IRenderPass3D renderPass, IReadOnlyList<ITexture2D> colorTextures, ITexture2D depthTexture)
    {
        return Vulkan.CreateFramebuffer3D(renderPass, colorTextures, depthTexture);
    }

    public IPipeline3D CreatePipeline3D(
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings,
        VertexInputDescription vertexInput,
        PipelineOptions? options = null)
    {
        return Vulkan.CreatePipeline3D(renderPass, vertexShaderSpirv, fragmentShaderSpirv, descriptorBindings, vertexInput, options);
    }

    public IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes)
    {
        return Vulkan.CreateDescriptorSet(pipeline, poolSizes);
    }

    public ISampler CreateSampler(
        SamplerFilter minFilter = SamplerFilter.Linear,
        SamplerFilter magFilter = SamplerFilter.Linear,
        SamplerAddressMode addressModeU = SamplerAddressMode.ClampToEdge,
        SamplerAddressMode addressModeV = SamplerAddressMode.ClampToEdge)
    {
        return Vulkan.CreateSampler(minFilter, magFilter, addressModeU, addressModeV);
    }

    public void CopyBuffer(IBuffer source, IBuffer destination, ulong size)
    {
        Vulkan.CopyBuffer(source, destination, size);
    }


    public void CopyTexture(ITexture2D source, ITexture2D destination)
    {
        Vulkan.CopyTexture(source, destination);
    }

    public void CopyTextureToCubeFace(ITexture2D source, ITextureCube destination, int faceIndex)
    {
        Vulkan.CopyTextureToCubeFace(source, destination, faceIndex);
    }


    public void CopyTextureToArrayLayer(ITexture2D source, ITextureArray destination, int layerIndex)
    {
        Vulkan.CopyTextureToArrayLayer(source, destination, layerIndex);
    }

    public void CopyTextureToCubeArrayFace(ITexture2D source, ITextureCubeArray destination, int arrayIndex, int faceIndex)
    {
        Vulkan.CopyTextureToCubeArrayFace(source, destination, arrayIndex, faceIndex);
    }

    public void WaitIdle()
    {
        Vulkan.WaitIdle();
        Metal?.WaitIdle();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Vulkan.Dispose();
        Metal?.Dispose();
    }
}
