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

    public void Apply<T>(CustomFilterEffectContext context, T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IGraphicsContext? graphicsContext = GraphicsContextFactory.SharedContext;
        if (graphicsContext == null || !graphicsContext.Supports3DRendering)
            return;

        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            RenderTarget? renderTarget = target.RenderTarget;

            if (renderTarget == null)
                continue;

            ITexture2D? sourceTexture = renderTarget.Texture;
            if (sourceTexture == null)
                continue;

            renderTarget.PrepareForSampling();

            EffectTarget newTarget = context.CreateTarget(target.Bounds);
            RenderTarget? newRenderTarget = newTarget.RenderTarget;

            if (newRenderTarget?.Texture == null)
            {
                newTarget.Dispose();
                continue;
            }

            ITexture2D destinationTexture = newRenderTarget.Texture;
            try
            {
                using ITexture2D depthTexture = graphicsContext.CreateTexture2D(
                    destinationTexture.Width,
                    destinationTexture.Height,
                    TextureFormat.Depth32Float);

                _pipeline.Execute(sourceTexture, destinationTexture, depthTexture, pushConstants);

                target.Dispose();
                context.Targets[i] = newTarget;
            }
            catch
            {
                newTarget.Dispose();
                throw;
            }
        }
    }

    public void Apply<T>(
        CustomFilterEffectContext context,
        Func<EffectTarget, T> createPushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IGraphicsContext? graphicsContext = GraphicsContextFactory.SharedContext;
        if (graphicsContext == null || !graphicsContext.Supports3DRendering)
            return;

        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            RenderTarget? renderTarget = target.RenderTarget;

            if (renderTarget == null)
                continue;

            ITexture2D? sourceTexture = renderTarget.Texture;
            if (sourceTexture == null)
                continue;

            renderTarget.PrepareForSampling();

            EffectTarget newTarget = context.CreateTarget(target.Bounds);
            RenderTarget? newRenderTarget = newTarget.RenderTarget;

            if (newRenderTarget?.Texture == null)
            {
                newTarget.Dispose();
                continue;
            }

            ITexture2D destinationTexture = newRenderTarget.Texture;

            try
            {
                using ITexture2D depthTexture = graphicsContext.CreateTexture2D(
                    destinationTexture.Width,
                    destinationTexture.Height,
                    TextureFormat.Depth32Float);

                T pushConstants = createPushConstants(target);
                _pipeline.Execute(sourceTexture, destinationTexture, depthTexture, pushConstants);

                target.Dispose();
                context.Targets[i] = newTarget;
            }
            catch
            {
                newTarget.Dispose();
                throw;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeline.Dispose();
    }
}
