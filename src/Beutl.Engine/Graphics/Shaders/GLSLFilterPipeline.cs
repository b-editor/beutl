using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Shaders;

internal sealed class GLSLFilterPipeline : IDisposable
{
    // Vulkan guarantees at least 16 fragment samplers. Unknown context implementations use that portable limit.
    internal const int PortableInputLimit = 16;

    internal static int GetMaximumInputCount(IGraphicsContext context)
        => TryResolveVulkan(context) is { } vulkan ? vulkan.MaxFragmentShaderInputTextures : PortableInputLimit;

    /// <summary>Gets the Vulkan context that records this pipeline's passes, directly or behind a composite.</summary>
    internal static VulkanContext? TryResolveVulkan(IGraphicsContext? context)
        => context switch
        {
            VulkanContext vulkan => vulkan,
            CompositeContext composite => composite.Vulkan,
            _ => null,
        };

    internal static void ValidateInputCount(IGraphicsContext context, int inputCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inputCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(inputCount, GetMaximumInputCount(context));
    }

    private static readonly ILogger s_logger = Log.CreateLogger<GLSLFilterPipeline>();

    // Fullscreen triangle vertex shader that generates UV coordinates
    private const string FullscreenVertexShader = """
        #version 450

        layout(location = 0) out vec2 fragCoord;

        void main() {
            // Generate fullscreen triangle vertices
            // Vertex 0: (-1, -1), UV (0, 0)
            // Vertex 1: (3, -1), UV (2, 0)
            // Vertex 2: (-1, 3), UV (0, 2)
            vec2 positions[3] = vec2[](
                vec2(-1.0, -1.0),
                vec2(3.0, -1.0),
                vec2(-1.0, 3.0)
            );
            vec2 uvs[3] = vec2[](
                vec2(0.0, 0.0),
                vec2(2.0, 0.0),
                vec2(0.0, 2.0)
            );
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragCoord = uvs[gl_VertexIndex];
        }
        """;

    private readonly IGraphicsContext _context;
    private readonly IRenderPass3D _renderPass;
    private readonly IPipeline3D _pipeline;
    private readonly ISampler _sampler;
    private readonly long _retainedByteSize;
    private readonly ShaderOutputCoverage _outputCoverage;
    private bool _disposed;

    internal int InputCount { get; }

    /// <summary>
    /// Gets the compiled-bytecode weight used to bound the cache. Driver-owned pipeline memory is not observable.
    /// </summary>
    internal long RetainedByteSize => _retainedByteSize;

    private GLSLFilterPipeline(
        IGraphicsContext context,
        IRenderPass3D renderPass,
        IPipeline3D pipeline,
        ISampler sampler,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        ShaderOutputCoverage outputCoverage,
        int inputCount)
    {
        _context = context;
        _renderPass = renderPass;
        _pipeline = pipeline;
        _sampler = sampler;
        _retainedByteSize = Math.Max(
            1,
            checked((long)vertexShaderSpirv.Length + fragmentShaderSpirv.Length));
        _outputCoverage = outputCoverage;
        InputCount = inputCount;
    }

    /// <summary>
    /// Compiles a fragment shader and creates its fullscreen filter pipeline.
    /// </summary>
    /// <param name="context">The graphics context that owns the pipeline.</param>
    /// <param name="fragmentShaderSource">The GLSL fragment shader source.</param>
    /// <param name="outputCoverage">
    /// The proven fragment-output coverage contract; see <see cref="ShaderOutputCoverage"/>.
    /// </param>
    /// <param name="inputCount">The number of consecutive sampler bindings in set 0.</param>
    public static GLSLFilterPipeline? Create(
        IGraphicsContext context,
        string fragmentShaderSource,
        ShaderOutputCoverage outputCoverage,
        int inputCount = 1)
    {
        ValidateInputCount(context, inputCount);
        if (!context.Supports3DRendering)
        {
            s_logger.LogWarning("3D rendering is not supported on this platform.");
            return null;
        }

        IRenderPass3D? renderPass = null;
        IPipeline3D? pipeline = null;
        ISampler? sampler = null;
        try
        {
            (byte[] vertexShaderSpirv, byte[] fragmentShaderSpirv) = CompileStages(context, fragmentShaderSource);

            renderPass = context.CreateRenderPass3D(
                [TextureFormat.RGBA16Float],
                depthFormat: null,
                colorLoadOp: outputCoverage == ShaderOutputCoverage.ProvablyFull
                    ? AttachmentLoadOp.DontCare
                    : AttachmentLoadOp.Clear);

            sampler = context.CreateSampler(
                SamplerFilter.Linear,
                SamplerFilter.Linear,
                SamplerAddressMode.ClampToEdge,
                SamplerAddressMode.ClampToEdge);

            var descriptorBindings = new DescriptorBinding[inputCount];
            for (int i = 0; i < inputCount; i++)
            {
                descriptorBindings[i] = new(
                    (uint)i, DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment);
            }

            pipeline = context.CreatePipeline3D(
                renderPass,
                vertexShaderSpirv,
                fragmentShaderSpirv,
                descriptorBindings,
                VertexInputDescription.Empty,
                PipelineOptions.Fullscreen);

            return new GLSLFilterPipeline(
                context,
                renderPass,
                pipeline,
                sampler,
                vertexShaderSpirv,
                fragmentShaderSpirv,
                outputCoverage,
                inputCount);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to create GLSL filter pipeline.");
            DisposeAfterFailedCreation(pipeline);
            DisposeAfterFailedCreation(renderPass);
            DisposeAfterFailedCreation(sampler);
            return null;
        }
    }

    /// <summary>
    /// Compiles the fullscreen vertex stage and <paramref name="fragmentShaderSource"/> to SPIR-V, releasing the
    /// compiler before any GPU object is created.
    /// </summary>
    private static (byte[] Vertex, byte[] Fragment) CompileStages(IGraphicsContext context, string fragmentShaderSource)
    {
        IShaderCompiler compiler = context.CreateShaderCompiler();
        using IDisposable? compilerLifetime = compiler as IDisposable;
        byte[] vertexShaderSpirv = compiler.CompileToSpirv(FullscreenVertexShader, ShaderStage.Vertex);
        byte[] fragmentShaderSpirv = compiler.CompileToSpirv(fragmentShaderSource, ShaderStage.Fragment);
        return (vertexShaderSpirv, fragmentShaderSpirv);
    }

    private static void DisposeAfterFailedCreation(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to release a resource of a GLSL filter pipeline that could not be created.");
        }
    }

    public void Execute<T>(
        ITexture2D sourceTexture,
        ITexture2D destinationTexture,
        T pushConstants) where T : unmanaged
        => Execute([sourceTexture], destinationTexture, pushConstants);

    public void Execute<T>(
        ReadOnlySpan<ITexture2D> inputTextures,
        ITexture2D destinationTexture,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (inputTextures.Length != InputCount)
            throw new ArgumentException($"The shader requires {InputCount} input textures.", nameof(inputTextures));

        foreach (ITexture2D texture in inputTextures)
            texture.PrepareForSampling();
        PrepareDestination(destinationTexture);

        using IFramebuffer3D framebuffer = _context.CreateFramebuffer3D(
            _renderPass,
            [destinationTexture],
            depthTexture: null);

        // Bind each input at its corresponding set-0 sampler binding.
        using IDescriptorSet descriptorSet = _context.CreateDescriptorSet(
            _pipeline,
            [new DescriptorPoolSize(DescriptorType.CombinedImageSampler, (uint)InputCount)]);
        for (int i = 0; i < inputTextures.Length; i++)
            descriptorSet.UpdateTexture(i, inputTextures[i], _sampler);

        // The pass holds a render-pass scope on the context-wide recording batch, so a body that
        // throws has to release it or every later transfer in the process is diverted.
        _renderPass.Begin(framebuffer, [default]);
        try
        {
            _renderPass.BindPipeline(_pipeline);
            _renderPass.BindDescriptorSet(_pipeline, descriptorSet);
            _renderPass.SetPushConstants(pushConstants);
            _renderPass.Draw(3); // Fullscreen triangle
        }
        finally
        {
            _renderPass.End();
        }

        destinationTexture.PrepareForSampling();
    }

    private void PrepareDestination(ITexture2D destinationTexture)
    {
        if (_outputCoverage == ShaderOutputCoverage.MayLeavePixelsUnwritten)
        {
            if (destinationTexture is not ITransparentClearableTexture clearableTexture)
            {
                throw new InvalidOperationException(
                    "A conservative native shader requires an ordered transparent-clear texture.");
            }

            clearableTexture.ClearToTransparent();
        }

        destinationTexture.PrepareForRender();
    }

    internal void SubmitPendingCommands()
    {
        // A subsequent effect may consume this output through another backend immediately. Submit the recorded
        // clears and draws so their queue order is established before the caller releases the source target.
        VulkanContext context = TryResolveVulkan(_context)
            ?? throw new InvalidOperationException("The GLSL pipeline requires a Vulkan recording context.");
        context.FlushCommands(waitForCompletion: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _pipeline.Dispose();
        }
        finally
        {
            try
            {
                _renderPass.Dispose();
            }
            finally
            {
                _sampler.Dispose();
            }
        }
    }
}
