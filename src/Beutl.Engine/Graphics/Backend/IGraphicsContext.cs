using System.Collections.Generic;
using Beutl.Graphics3D;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

public interface IGraphicsContext : IDisposable
{
    GraphicsBackend Backend { get; }

    GRContext SkiaContext { get; }

    /// <summary>
    /// Gets a value indicating whether 3D rendering is supported.
    /// </summary>
    bool Supports3DRendering { get; }

    /// <summary>
    /// Creates a 2D texture.
    /// </summary>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="format">The texture format.</param>
    ITexture2D CreateTexture2D(int width, int height, TextureFormat format);

    /// <summary>
    /// Creates a cube map texture (for point light shadow maps).
    /// </summary>
    /// <param name="size">The size (width and height) of each cube face.</param>
    /// <param name="format">The texture format.</param>
    ITextureCube CreateTextureCube(int size, TextureFormat format);

    /// <summary>
    /// Creates a 2D texture array (for multiple shadow maps).
    /// </summary>
    /// <param name="width">The width of each layer.</param>
    /// <param name="height">The height of each layer.</param>
    /// <param name="arraySize">The number of layers in the array.</param>
    /// <param name="format">The texture format.</param>
    ITextureArray CreateTextureArray(int width, int height, uint arraySize, TextureFormat format);

    /// <summary>
    /// Creates a cube map texture array (for multiple point light shadow maps).
    /// </summary>
    /// <param name="size">The size (width and height) of each cube face.</param>
    /// <param name="arraySize">The number of cube maps in the array.</param>
    /// <param name="format">The texture format.</param>
    ITextureCubeArray CreateTextureCubeArray(int size, uint arraySize, TextureFormat format);

    /// <summary>
    /// Creates a new GPU buffer.
    /// </summary>
    IBuffer CreateBuffer(ulong size, BufferUsage usage, MemoryProperty memoryProperty);

    /// <summary>
    /// Creates a new shader compiler.
    /// </summary>
    IShaderCompiler CreateShaderCompiler();

    /// <summary>
    /// Creates a new 3D render pass with multiple color attachments and specified load operations.
    /// </summary>
    /// <param name="colorFormats">Formats for each color attachment.</param>
    /// <param name="depthFormat">Format for the depth attachment.</param>
    /// <param name="colorLoadOp">The load operation for color attachments.</param>
    /// <param name="depthLoadOp">The load operation for the depth attachment.</param>
    IRenderPass3D CreateRenderPass3D(
        IReadOnlyList<TextureFormat> colorFormats,
        TextureFormat depthFormat = TextureFormat.Depth32Float,
        AttachmentLoadOp colorLoadOp = AttachmentLoadOp.Clear,
        AttachmentLoadOp depthLoadOp = AttachmentLoadOp.Clear);

    /// <summary>
    /// Creates a new 3D framebuffer with multiple color attachments.
    /// </summary>
    /// <param name="renderPass">The render pass to use with this framebuffer.</param>
    /// <param name="colorTextures">The color attachment textures.</param>
    /// <param name="depthTexture">The depth attachment texture.</param>
    IFramebuffer3D CreateFramebuffer3D(IRenderPass3D renderPass, IReadOnlyList<ITexture2D> colorTextures, ITexture2D depthTexture);

    /// <summary>
    /// Creates a new 3D pipeline.
    /// </summary>
    /// <param name="renderPass">The render pass for the pipeline.</param>
    /// <param name="vertexShaderSpirv">The compiled vertex shader SPIR-V bytecode.</param>
    /// <param name="fragmentShaderSpirv">The compiled fragment shader SPIR-V bytecode.</param>
    /// <param name="descriptorBindings">The descriptor bindings for the pipeline.</param>
    /// <param name="vertexInput">The vertex input description. Use VertexInputDescription.Empty for fullscreen passes.</param>
    /// <param name="options">Pipeline options (depth test, cull mode, etc.). If null, uses PipelineOptions.Default.</param>
    /// <returns>A new pipeline instance.</returns>
    IPipeline3D CreatePipeline3D(
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings,
        VertexInputDescription vertexInput,
        PipelineOptions? options = null);

    /// <summary>
    /// Creates a new descriptor set.
    /// </summary>
    IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes);

    /// <summary>
    /// Creates a new texture sampler.
    /// </summary>
    /// <param name="minFilter">The minification filter.</param>
    /// <param name="magFilter">The magnification filter.</param>
    /// <param name="addressModeU">The address mode for U coordinate.</param>
    /// <param name="addressModeV">The address mode for V coordinate.</param>
    ISampler CreateSampler(
        SamplerFilter minFilter = SamplerFilter.Linear,
        SamplerFilter magFilter = SamplerFilter.Linear,
        SamplerAddressMode addressModeU = SamplerAddressMode.ClampToEdge,
        SamplerAddressMode addressModeV = SamplerAddressMode.ClampToEdge);

    /// <summary>
    /// Copies data between two buffers.
    /// </summary>
    void CopyBuffer(IBuffer source, IBuffer destination, ulong size);

    /// <summary>
    /// Copies texture data from source to destination.
    /// </summary>
    /// <param name="source">The source texture.</param>
    /// <param name="destination">The destination texture.</param>
    void CopyTexture(ITexture2D source, ITexture2D destination);

    /// <summary>
    /// Copies a 2D texture to a specific face of a cube map.
    /// </summary>
    /// <param name="source">The source 2D texture.</param>
    /// <param name="destination">The destination cube map texture.</param>
    /// <param name="faceIndex">The cube face index (0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z).</param>
    void CopyTextureToCubeFace(ITexture2D source, ITextureCube destination, int faceIndex);

    /// <summary>
    /// Copies a 2D texture to a specific layer of a texture array.
    /// </summary>
    /// <param name="source">The source 2D texture.</param>
    /// <param name="destination">The destination texture array.</param>
    /// <param name="layerIndex">The layer index in the array.</param>
    void CopyTextureToArrayLayer(ITexture2D source, ITextureArray destination, int layerIndex);

    /// <summary>
    /// Copies a 2D texture to a specific face of a cube map in a cube array.
    /// </summary>
    /// <param name="source">The source 2D texture.</param>
    /// <param name="destination">The destination cube map array texture.</param>
    /// <param name="arrayIndex">The array index of the cube map.</param>
    /// <param name="faceIndex">The cube face index (0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z).</param>
    void CopyTextureToCubeArrayFace(ITexture2D source, ITextureCubeArray destination, int arrayIndex, int faceIndex);

    void WaitIdle();
}
