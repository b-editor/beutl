using Beutl.Graphics3D;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

internal interface IGraphicsContext : IDisposable
{
    GraphicsBackend Backend { get; }

    GRContext SkiaContext { get; }

    /// <summary>
    /// Gets a value indicating whether 3D rendering is supported.
    /// </summary>
    bool Supports3DRendering { get; }

    ISharedTexture CreateTexture(int width, int height, TextureFormat format);

    /// <summary>
    /// Creates a new 3D renderer.
    /// </summary>
    /// <returns>A new 3D renderer instance.</returns>
    /// <exception cref="NotSupportedException">Thrown if 3D rendering is not supported.</exception>
    I3DRenderer Create3DRenderer();

    /// <summary>
    /// Creates a new GPU buffer.
    /// </summary>
    /// <param name="size">The size of the buffer in bytes.</param>
    /// <param name="usage">The intended usage of the buffer.</param>
    /// <param name="memoryProperty">The memory properties for the buffer.</param>
    /// <returns>A new buffer instance.</returns>
    IBuffer CreateBuffer(ulong size, BufferUsage usage, MemoryProperty memoryProperty);

    /// <summary>
    /// Creates a new shader compiler.
    /// </summary>
    /// <returns>A new shader compiler instance.</returns>
    IShaderCompiler CreateShaderCompiler();

    /// <summary>
    /// Creates a new 3D render pass.
    /// </summary>
    /// <returns>A new render pass instance.</returns>
    IRenderPass3D CreateRenderPass3D();

    /// <summary>
    /// Creates a new 3D framebuffer.
    /// </summary>
    /// <param name="renderPass">The render pass to use with this framebuffer.</param>
    /// <param name="colorTexture">The color attachment texture.</param>
    /// <returns>A new framebuffer instance.</returns>
    IFramebuffer3D CreateFramebuffer3D(IRenderPass3D renderPass, ISharedTexture colorTexture);

    /// <summary>
    /// Creates a new 3D pipeline.
    /// </summary>
    /// <param name="renderPass">The render pass for the pipeline.</param>
    /// <param name="vertexShaderSpirv">The compiled vertex shader SPIR-V bytecode.</param>
    /// <param name="fragmentShaderSpirv">The compiled fragment shader SPIR-V bytecode.</param>
    /// <param name="descriptorBindings">The descriptor bindings for the pipeline.</param>
    /// <returns>A new pipeline instance.</returns>
    IPipeline3D CreatePipeline3D(
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings);

    /// <summary>
    /// Creates a new descriptor set.
    /// </summary>
    /// <param name="pipeline">The pipeline that defines the descriptor layout.</param>
    /// <param name="poolSizes">The pool sizes for descriptor allocation.</param>
    /// <returns>A new descriptor set instance.</returns>
    IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes);

    /// <summary>
    /// Copies data between two buffers.
    /// </summary>
    /// <param name="source">The source buffer.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="size">The number of bytes to copy.</param>
    void CopyBuffer(IBuffer source, IBuffer destination, ulong size);

    void WaitIdle();
}
