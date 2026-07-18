using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public sealed class GLSLShader : IDisposable
{
    private readonly GLSLFilterPipeline _pipeline;
    private bool _disposed;

    private GLSLShader(GLSLFilterPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public static GLSLShader Create(string fragmentShaderSource)
    {
        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null || !context.Supports3DRendering)
        {
            throw new InvalidOperationException("Vulkan 3D rendering is not supported on this platform.");
        }

        GLSLFilterPipeline? pipeline = GLSLFilterPipeline.Create(context, fragmentShaderSource);
        if (pipeline == null)
        {
            throw new InvalidOperationException("Failed to compile GLSL shader.");
        }

        return new GLSLShader(pipeline);
    }

    // Creates a shader that reads from two textures (binding 0 = source, binding 1 = mask)
    public static GLSLShader CreateDualTexture(string fragmentShaderSource)
    {
        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null || !context.Supports3DRendering)
        {
            throw new InvalidOperationException("Vulkan 3D rendering is not supported on this platform.");
        }

        GLSLFilterPipeline? pipeline = GLSLFilterPipeline.Create(context, fragmentShaderSource, hasMaskTexture: true);
        if (pipeline == null)
        {
            throw new InvalidOperationException("Failed to compile GLSL dual-texture shader.");
        }

        return new GLSLShader(pipeline);
    }

    public static bool TryCreate(string fragmentShaderSource, out GLSLShader? shader, out string? errorText)
    {
        shader = null;
        errorText = null;

        if (string.IsNullOrWhiteSpace(fragmentShaderSource))
        {
            errorText = "Fragment shader source is empty.";
            return false;
        }

        IGraphicsContext? context = GraphicsContextFactory.SharedContext;
        if (context == null || !context.Supports3DRendering)
        {
            errorText = "Vulkan 3D rendering is not supported on this platform.";
            return false;
        }

        try
        {
            GLSLFilterPipeline? pipeline = GLSLFilterPipeline.Create(context, fragmentShaderSource);
            if (pipeline == null)
            {
                errorText = "Failed to compile GLSL shader.";
                return false;
            }

            shader = new GLSLShader(pipeline);
            return true;
        }
        catch (Exception ex)
        {
            errorText = ex.Message;
            return false;
        }
    }

    internal GLSLFilterPipeline Pipeline
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _pipeline;
        }
    }

    // Execute a single pass against specific textures (for use by multi-pass effects)
    internal void ExecuteSingleTarget<T>(
        ITexture2D source,
        ITexture2D destination,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pipeline.Execute(source, destination, pushConstants);
    }

    // Execute a single pass with mask texture (for use by multi-pass effects)
    internal void ExecuteSingleTargetWithMask<T>(
        ITexture2D source,
        ITexture2D mask,
        ITexture2D destination,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pipeline.Execute(source, mask, destination, pushConstants);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeline.Dispose();
    }
}
