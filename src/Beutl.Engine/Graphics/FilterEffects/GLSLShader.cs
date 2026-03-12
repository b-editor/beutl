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

    public void Apply<T>(CustomFilterEffectContext context, T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_pipeline.HasMaskTexture)
            throw new InvalidOperationException("Cannot use single-texture Apply on a dual-texture shader. Use ExecuteSingleTargetWithMask instead.");

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

    // Execute a single pass against specific textures (for use by multi-pass effects)
    internal void ExecuteSingleTarget<T>(
        ITexture2D source,
        ITexture2D destination,
        ITexture2D depth,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pipeline.Execute(source, destination, depth, pushConstants);
    }

    // Execute a single pass with mask texture (for use by multi-pass effects)
    internal void ExecuteSingleTargetWithMask<T>(
        ITexture2D source,
        ITexture2D mask,
        ITexture2D destination,
        ITexture2D depth,
        T pushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pipeline.Execute(source, mask, destination, depth, pushConstants);
    }

    // Multi-pass apply with ping-pong intermediate textures
    public void ApplyMultiPass<T>(
        CustomFilterEffectContext context,
        int passCount,
        Func<int, EffectTarget, T> createPushConstants) where T : unmanaged
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

            int width = sourceTexture.Width;
            int height = sourceTexture.Height;

            // Create ping-pong textures
            using ITexture2D pingTexture = graphicsContext.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
            using ITexture2D pongTexture = graphicsContext.CreateTexture2D(width, height, TextureFormat.BGRA8Unorm);
            using ITexture2D depthTexture = graphicsContext.CreateTexture2D(width, height, TextureFormat.Depth32Float);

            // Run first shader pass (pass 0) from source into ping buffer as the initial state
            sourceTexture.PrepareForSampling();

            if (passCount == 1)
            {
                // Single pass: write directly to the new EffectTarget
                EffectTarget newTarget = context.CreateTarget(target.Bounds);
                RenderTarget? newRenderTarget = newTarget.RenderTarget;

                if (newRenderTarget?.Texture == null)
                {
                    newTarget.Dispose();
                    continue;
                }

                try
                {
                    _pipeline.Execute(sourceTexture, newRenderTarget.Texture, depthTexture, createPushConstants(0, target));

                    target.Dispose();
                    context.Targets[i] = newTarget;
                }
                catch
                {
                    newTarget.Dispose();
                    throw;
                }

                continue;
            }

            _pipeline.Execute(sourceTexture, pingTexture, depthTexture, createPushConstants(0, target));

            ITexture2D current = pingTexture;
            ITexture2D next = pongTexture;

            // Run intermediate passes with ping-pong (passes 1 to passCount-2)
            for (int pass = 1; pass < passCount - 1; pass++)
            {
                _pipeline.Execute(current, next, depthTexture, createPushConstants(pass, target));
                (current, next) = (next, current);
            }

            // Final pass: write directly to the new EffectTarget
            {
                EffectTarget newTarget = context.CreateTarget(target.Bounds);
                RenderTarget? newRenderTarget = newTarget.RenderTarget;

                if (newRenderTarget?.Texture == null)
                {
                    newTarget.Dispose();
                    continue;
                }

                try
                {
                    _pipeline.Execute(current, newRenderTarget.Texture, depthTexture, createPushConstants(passCount - 1, target));

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
