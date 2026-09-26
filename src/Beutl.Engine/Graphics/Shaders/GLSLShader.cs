using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Shaders;

public sealed class GLSLShader : IDisposable
{
    private const string No3DRenderingMessage = "Vulkan 3D rendering is not supported on this platform.";

    private readonly GLSLFilterPipeline _pipeline;
    private readonly IDisposable _pipelineLifetime;
    private bool _disposed;

    private GLSLShader(GLSLFilterPipeline pipeline)
    {
        _pipeline = pipeline;
        _pipelineLifetime = pipeline;
    }

    internal GLSLShader(ProgramCacheLease<GLSLFilterPipeline> lease)
    {
        _pipeline = lease.Program;
        _pipelineLifetime = lease;
    }

    /// <summary>Gets the shared graphics context, or <see langword="null"/> when it cannot run a pipeline.</summary>
    private static IGraphicsContext? TryGetGraphicsContext()
        => GraphicsContextFactory.SharedContext is { Supports3DRendering: true } context ? context : null;

    /// <inheritdoc cref="TryGetGraphicsContext"/>
    /// <exception cref="InvalidOperationException">The platform cannot run a 3D pipeline.</exception>
    private static IGraphicsContext RequireGraphicsContext()
        => TryGetGraphicsContext() ?? throw new InvalidOperationException(No3DRenderingMessage);

    public static GLSLShader Create(string fragmentShaderSource)
        => Create(fragmentShaderSource, 1);

    /// <summary>Creates a GLSL fragment program with consecutive sampler2D bindings in set 0.</summary>
    /// <param name="fragmentShaderSource">GLSL source whose output is linear, premultiplied RGBA.</param>
    /// <param name="inputCount">Number of input samplers, starting at binding 0.</param>
    public static GLSLShader Create(string fragmentShaderSource, int inputCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(inputCount, 1);
        IGraphicsContext context = RequireGraphicsContext();
        GLSLFilterPipeline.ValidateInputCount(context, inputCount);

        GLSLFilterPipeline? pipeline = GLSLFilterPipeline.Create(
            context,
            fragmentShaderSource,
            ShaderOutputCoverage.MayLeavePixelsUnwritten,
            inputCount: inputCount);
        if (pipeline == null)
        {
            throw new InvalidOperationException("Failed to compile GLSL shader.");
        }

        return new GLSLShader(pipeline);
    }

    /// <summary>Gets the number of input targets required by Render.</summary>
    public int InputCount => Pipeline.InputCount;

    /// <summary>Gets the active backend's maximum number of fragment input textures.</summary>
    public static int MaximumInputCount => GLSLFilterPipeline.GetMaximumInputCount(RequireGraphicsContext());

    /// <summary>Renders one GLSL pass into a new target without replacing or disposing its inputs.</summary>
    /// <remarks>
    /// Call inside Context.CustomEffect. The caller owns the result: dispose intermediates and return the
    /// final target from CustomFilterEffectContext.ForEach to replace the source. Pass earlier results back
    /// as inputs to build multiple passes. The shader receives normalized fragCoord over the destination;
    /// each sampler addresses its own complete input texture. Use input/output RasterBounds and Scale to
    /// map effect-local positions between textures, including padding and different resolutions.
    /// Push constants follow the shader's declared layout, with a 4-byte-aligned size of at most 128 bytes.
    /// An empty result means preview allocation was declined; keep the input in that case. Delivery throws.
    /// </remarks>
    public EffectTarget Render<T>(
        CustomFilterEffectContext context,
        IReadOnlyList<EffectTarget> inputs,
        Rect outputBounds,
        T pushConstants) where T : unmanaged
        => Render<T, T>(context, inputs, outputBounds, pushConstants, static (_, value) => value);

    /// <summary>Renders a pass with constants derived from the actual destination size and clamped density.</summary>
    /// <inheritdoc cref="Render{T}(CustomFilterEffectContext, IReadOnlyList{EffectTarget}, Rect, T)" path="/remarks"/>
    public EffectTarget Render<T>(
        CustomFilterEffectContext context,
        IReadOnlyList<EffectTarget> inputs,
        Rect outputBounds,
        Func<EffectTarget, T> createPushConstants) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(createPushConstants);
        return Render<T, Func<EffectTarget, T>>(
            context, inputs, outputBounds, createPushConstants, static (output, factory) => factory(output));
    }

    private EffectTarget Render<T, TState>(
        CustomFilterEffectContext context,
        IReadOnlyList<EffectTarget> inputs,
        Rect outputBounds,
        TState state,
        Func<EffectTarget, TState, T> createPushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count != _pipeline.InputCount)
            throw new ArgumentException($"The shader requires {_pipeline.InputCount} input targets.", nameof(inputs));
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        if (outputBounds.Width <= 0 || outputBounds.Height <= 0)
            throw new ArgumentException("Output dimensions must be positive.", nameof(outputBounds));
        int constantSize = Unsafe.SizeOf<T>();
        if (constantSize > 128 || constantSize % 4 != 0)
            throw new ArgumentException("Push constants must occupy a multiple of 4 bytes, up to 128 bytes.", nameof(createPushConstants));

        if (inputs.Count == 1)
        {
            ITexture2D texture = GetInputTexture(inputs, 0);
            return RenderWithTextures(context, inputs, [texture], outputBounds, state, createPushConstants);
        }

        ITexture2D[] textures = ArrayPool<ITexture2D>.Shared.Rent(inputs.Count);
        try
        {
            for (int i = 0; i < inputs.Count; i++)
                textures[i] = GetInputTexture(inputs, i);
            return RenderWithTextures(
                context, inputs, textures.AsSpan(0, inputs.Count), outputBounds, state, createPushConstants);
        }
        finally
        {
            ArrayPool<ITexture2D>.Shared.Return(textures, clearArray: true);
        }
    }

    private static ITexture2D GetInputTexture(IReadOnlyList<EffectTarget> inputs, int index)
        => inputs[index]?.RenderTarget?.Texture
           ?? throw new ArgumentException($"Input {index} must have a materialized GPU texture.", nameof(inputs));

    private EffectTarget RenderWithTextures<T, TState>(
        CustomFilterEffectContext context,
        IReadOnlyList<EffectTarget> inputs,
        ReadOnlySpan<ITexture2D> textures,
        Rect outputBounds,
        TState state,
        Func<EffectTarget, TState, T> createPushConstants) where T : unmanaged
    {
        EffectTarget output = outputBounds == inputs[0].Bounds
            ? context.CreateNativeTargetLike(inputs[0])
            : context.CreateNativeTarget(outputBounds);
        if (output.IsEmpty)
            return output;
        try
        {
            if (output.RenderTarget?.Texture is not { } destination)
                throw new InvalidOperationException("The destination must have a GPU texture.");
            T constants = createPushConstants(output, state);
            for (int i = 0; i < inputs.Count; i++)
                inputs[i].RenderTarget!.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);
            _pipeline.Execute(textures, destination, constants);
            _pipeline.SubmitPendingCommands();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    // Creates a shader that reads from two textures (binding 0 = source, binding 1 = mask)
    public static GLSLShader CreateDualTexture(string fragmentShaderSource)
    {
        IGraphicsContext context = RequireGraphicsContext();

        GLSLFilterPipeline? pipeline = GLSLFilterPipeline.Create(
            context,
            fragmentShaderSource,
            ShaderOutputCoverage.MayLeavePixelsUnwritten,
            hasMaskTexture: true);
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

        if (TryGetGraphicsContext() is not { } context)
        {
            errorText = No3DRenderingMessage;
            return false;
        }

        try
        {
            GLSLFilterPipeline? pipeline = GLSLFilterPipeline.Create(
                context,
                fragmentShaderSource,
                ShaderOutputCoverage.MayLeavePixelsUnwritten);
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

    /// <summary>
    /// Creates an engine-owned shader whose output is allowed to skip pooled-target initialization.
    /// </summary>
    /// <param name="fragmentShaderSource">
    /// An audited built-in fragment shader. Every control-flow path must write the fragment output and the shader
    /// must contain no <c>discard</c>. If this proof is false, unwritten pixels can expose stale data from a
    /// previous unrelated frame when a pooled target is warm.
    /// </param>
    /// <param name="specializationConstants">Immutable values fixed for the lifetime of the created pipeline.</param>
    /// <param name="hasMaskTexture">Whether the shader reads a second texture at binding 1.</param>
    internal static GLSLShader CreateBuiltIn(
        string fragmentShaderSource,
        ImmutableArray<SpecializationConstant> specializationConstants = default,
        bool hasMaskTexture = false)
    {
        IGraphicsContext context = RequireGraphicsContext();

        GLSLFilterPipeline? pipeline = GLSLFilterPipeline.Create(
            context,
            fragmentShaderSource,
            ShaderOutputCoverage.ProvablyFull,
            specializationConstants,
            hasMaskTexture);
        if (pipeline == null)
        {
            throw new InvalidOperationException("Failed to compile built-in GLSL shader.");
        }

        return new GLSLShader(pipeline);
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

        if (TryGetGraphicsContext() is null)
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

            renderTarget.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);

            EffectTarget newTarget = context.CreateNativeTargetLike(target);
            RenderTarget? newRenderTarget = newTarget.RenderTarget;

            if (newRenderTarget?.Texture == null)
            {
                newTarget.Dispose();
                continue;
            }

            ITexture2D destinationTexture = newRenderTarget.Texture;
            try
            {
                _pipeline.Execute(sourceTexture, destinationTexture, pushConstants);
                _pipeline.SubmitPendingCommands();

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

    internal void SubmitPendingCommands()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pipeline.SubmitPendingCommands();
    }

    // Multi-pass apply with ping-pong intermediate textures
    public void ApplyMultiPass<T>(
        CustomFilterEffectContext context,
        int passCount,
        Func<int, EffectTarget, T> createPushConstants) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (TryGetGraphicsContext() is not { } graphicsContext)
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

            renderTarget.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);

            int width = sourceTexture.Width;
            int height = sourceTexture.Height;

            // Run first shader pass (pass 0) from source into ping buffer as the initial state
            sourceTexture.PrepareForSampling();

            if (passCount == 1)
            {
                // Single pass: write directly to the new EffectTarget
                EffectTarget newTarget = context.CreateNativeTargetLike(target);
                RenderTarget? newRenderTarget = newTarget.RenderTarget;

                if (newRenderTarget?.Texture == null)
                {
                    newTarget.Dispose();
                    continue;
                }

                try
                {
                    _pipeline.Execute(sourceTexture, newRenderTarget.Texture, createPushConstants(0, target));
                    _pipeline.SubmitPendingCommands();

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

            using NativeFilterTextureLease? pingLease = context.TryAcquireNativeScratchTexture(
                graphicsContext,
                width,
                height);
            if (pingLease is null)
                continue;

            using NativeFilterTextureLease? pongLease = context.TryAcquireNativeScratchTexture(
                graphicsContext,
                width,
                height);
            if (pongLease is null)
                continue;

            ITexture2D pingTexture = pingLease.Texture;
            ITexture2D pongTexture = pongLease.Texture;

            _pipeline.Execute(sourceTexture, pingTexture, createPushConstants(0, target));

            ITexture2D current = pingTexture;
            ITexture2D next = pongTexture;

            // Run intermediate passes with ping-pong (passes 1 to passCount-2)
            for (int pass = 1; pass < passCount - 1; pass++)
            {
                _pipeline.Execute(current, next, createPushConstants(pass, target));
                (current, next) = (next, current);
            }

            // Final pass: write directly to the new EffectTarget
            {
                EffectTarget newTarget = context.CreateNativeTargetLike(target);
                RenderTarget? newRenderTarget = newTarget.RenderTarget;

                if (newRenderTarget?.Texture == null)
                {
                    newTarget.Dispose();
                    continue;
                }

                try
                {
                    _pipeline.Execute(current, newRenderTarget.Texture, createPushConstants(passCount - 1, target));
                    _pipeline.SubmitPendingCommands();

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

        if (TryGetGraphicsContext() is null)
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

            renderTarget.PrepareForSampling(RenderTargetSamplingIntent.BackendInterop);

            EffectTarget newTarget = context.CreateNativeTargetLike(target);
            RenderTarget? newRenderTarget = newTarget.RenderTarget;

            if (newRenderTarget?.Texture == null)
            {
                newTarget.Dispose();
                continue;
            }

            ITexture2D destinationTexture = newRenderTarget.Texture;

            try
            {
                T pushConstants = createPushConstants(target);
                _pipeline.Execute(sourceTexture, destinationTexture, pushConstants);
                _pipeline.SubmitPendingCommands();

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
        _pipelineLifetime.Dispose();
    }
}
