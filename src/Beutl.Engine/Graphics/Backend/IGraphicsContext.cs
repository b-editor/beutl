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
    /// Creates a shared texture (for Skia interop).
    /// </summary>
    ISharedTexture CreateTexture(int width, int height, TextureFormat format);

    /// <summary>
    /// Creates an internal texture (not shared with Skia).
    /// </summary>
    ITexture2D CreateTexture2D(int width, int height, TextureFormat format);

    /// <summary>
    /// Creates a new 3D renderer.
    /// </summary>
    /// <returns>A new 3D renderer instance.</returns>
    /// <exception cref="NotSupportedException">Thrown if 3D rendering is not supported.</exception>
    I3DRenderer Create3DRenderer();

    /// <summary>
    /// Creates a new GPU buffer.
    /// </summary>
    IBuffer CreateBuffer(ulong size, BufferUsage usage, MemoryProperty memoryProperty);

    /// <summary>
    /// Creates a new shader compiler.
    /// </summary>
    IShaderCompiler CreateShaderCompiler();

    /// <summary>
    /// Creates a new 3D render pass with multiple color attachments.
    /// </summary>
    /// <param name="colorFormats">Formats for each color attachment.</param>
    /// <param name="depthFormat">Format for the depth attachment.</param>
    IRenderPass3D CreateRenderPass3D(IReadOnlyList<TextureFormat> colorFormats, TextureFormat depthFormat = TextureFormat.Depth32Float);

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
    IPipeline3D CreatePipeline3D(
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings);

    /// <summary>
    /// Creates a new descriptor set.
    /// </summary>
    IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes);

    /// <summary>
    /// Copies data between two buffers.
    /// </summary>
    void CopyBuffer(IBuffer source, IBuffer destination, ulong size);

    void WaitIdle();
}
