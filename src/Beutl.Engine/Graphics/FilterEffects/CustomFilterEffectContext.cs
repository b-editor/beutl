using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    private static readonly ILogger s_logger = Log.CreateLogger("CustomFilterEffectContext");
    private readonly Vector _deviceGridOffset;

    internal CustomFilterEffectContext(
        EffectTargets targets,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity,
        Vector? deviceGridOffset = null)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");

        Targets = targets;
        _deviceGridOffset = deviceGridOffset
            ?? (targets.Count > 0 ? targets[0].DeviceGridOffset : default);
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        Intent = intent;
        Purpose = purpose;
    }

    public EffectTargets Targets { get; }

    /// <summary>The render request's output scale <c>s_out</c>, not a ceiling on this effect's working scale.</summary>
    public float OutputScale { get; }

    /// <summary>
    /// The working density <c>w</c> this effect's buffers are allocated at: <see cref="CreateTarget"/>
    /// uses the near-edge/far-edge device footprint after applying <see cref="DeviceGridOffset"/>.
    /// Absolute-length pixel parameters must be multiplied by this.
    /// </summary>
    public float WorkingScale { get; }

    /// <summary>Working-scale ceiling forwarded into canvases from <see cref="Open"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; }

    /// <summary>
    /// Gets the translation from effect-local coordinates to the composition-device grid used
    /// for intermediate allocation.
    /// </summary>
    public Vector DeviceGridOffset => _deviceGridOffset;

    /// <summary>Gets the explicit preview or delivery classification for this execution.</summary>
    public RenderIntent Intent { get; }

    /// <summary>Gets the explicit request purpose for this execution.</summary>
    public RenderRequestPurpose Purpose { get; }

    public void ForEach(Action<int, EffectTarget> action)
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            EffectTarget target = Targets[i];
            action(i, target);
        }
    }

    public void ForEach(Func<int, EffectTarget, EffectTarget> action)
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            EffectTarget target = Targets[i];
            EffectTarget newTarget = action(i, target);
            if (newTarget != target)
            {
                target.Dispose();
                Targets[i] = newTarget;
            }
        }
    }

    public void ForEach(Func<int, EffectTarget, EffectTargets> action)
    {
        for (int i = 0; i < Targets.Count; i++)
        {
            using EffectTarget target = Targets[i];
            EffectTargets newTargets = action(i, target.Clone());

            Targets.RemoveAt(i);
            Targets.InsertRange(i, newTargets);
            i += newTargets.Count - 1;
        }
    }

    /// <summary>
    /// Device-buffer dimensions for a logical <paramref name="bounds"/> at density <paramref name="w"/>.
    /// This is <see cref="DeviceBufferBounds"/>'s size, including any extra rounding pixel caused by
    /// a fractional logical origin.
    /// </summary>
    public static (int Width, int Height) DeviceBufferSize(Rect bounds, float w)
    {
        PixelSize size = DeviceBufferBounds(bounds, w).Size;
        return (size.Width, size.Height);
    }

    /// <summary>
    /// Gets the canonical composition-device footprint allocated for logical bounds at a concrete density.
    /// The origin is retained because fractional logical positions can add a rounding pixel to the buffer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="w"/> is non-finite or not positive.
    /// </exception>
    public static PixelRect DeviceBufferBounds(Rect bounds, float w)
    {
        if (!float.IsFinite(w) || w <= 0)
            throw new ArgumentOutOfRangeException(nameof(w), w, "Buffer density must be positive and finite.");

        return PixelRect.FromRect(bounds, w);
    }

    /// <summary>
    /// The density <see cref="CreateTarget"/> will allocate for <paramref name="bounds"/>,
    /// after applying <see cref="DeviceGridOffset"/> and the per-buffer dimension clamp.
    /// </summary>
    public float ResolveTargetDensity(Rect bounds)
        => RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
            bounds.Translate(_deviceGridOffset),
            WorkingScale);

    public EffectTarget CreateTarget(Rect bounds)
        => CreateTargetCore(bounds, WorkingScale);

    internal EffectTarget CreateTargetAtMost(Rect bounds, float maximumDensity)
    {
        if (!float.IsFinite(maximumDensity) || maximumDensity <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumDensity),
                maximumDensity,
                "The target density ceiling must be positive and finite.");

        return CreateTargetCore(bounds, MathF.Min(WorkingScale, maximumDensity));
    }

    private EffectTarget CreateTargetCore(Rect bounds, float requestedDensity)
    {
        float w = requestedDensity;
        // Re-clamp at allocation site: bounds may exceed what node-level clamps saw.
        float fit = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(
            bounds.Translate(_deviceGridOffset),
            w);
        if (fit < w)
        {
            s_logger.LogWarning(
                "CreateTarget clamped the working scale {From} -> {To} to keep the buffer within the GPU axis limit (bounds {Bounds}). Use the returned target's Scale for output device math, not context.WorkingScale.",
                w, fit, bounds);
            w = fit;
        }

        PixelRect deviceBounds = DeviceBufferBounds(bounds.Translate(_deviceGridOffset), w);
        return AllocateTarget(bounds, w, deviceBounds, _deviceGridOffset);
    }

    /// <summary>
    /// Creates a replacement target with the source's complete physical footprint and current
    /// logical placement. Use this for same-bounds raster effects so fractional-origin pixels and
    /// raster aprons are preserved.
    /// </summary>
    public EffectTarget CreateTargetLike(EffectTarget source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            return new EffectTarget();

        using var renderTarget = RenderTarget.Create(source.DeviceBounds.Width, source.DeviceBounds.Height);
        if (renderTarget != null)
        {
            return source.CreateReplacement(renderTarget);
        }
        else
        {
            s_logger.LogWarning(
                "Custom-effect target allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); returning an empty target.",
                source.DeviceBounds.Width,
                source.DeviceBounds.Height,
                source.Scale.Value,
                source.Bounds);
            return new EffectTarget();
        }
    }

    /// <summary>
    /// Wraps a caller-created target as a replacement with the source's logical placement,
    /// density, physical footprint, and device-grid alignment.
    /// </summary>
    /// <remarks>
    /// The returned effect target owns a shallow copy; the caller retains ownership of
    /// <paramref name="renderTarget"/>.
    /// </remarks>
    public EffectTarget CreateReplacement(
        EffectTarget source,
        RenderTarget renderTarget)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(renderTarget);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
        {
            throw new ArgumentException(
                "The source must have a materialized target and concrete scale.",
                nameof(source));
        }
        if (renderTarget.Width != source.DeviceBounds.Width
            || renderTarget.Height != source.DeviceBounds.Height)
        {
            throw new ArgumentException(
                $"The replacement render target must match the source device footprint "
                + $"{source.DeviceBounds.Width}x{source.DeviceBounds.Height}.",
                nameof(renderTarget));
        }

        return source.CreateReplacement(renderTarget);
    }

    /// <summary>
    /// Creates a child shader that maps destination backing-buffer coordinates to the source
    /// target's current physical raster placement.
    /// </summary>
    /// <remarks>The caller owns and must dispose the returned shader.</remarks>
    public SKShader CreateMappedInputShader(
        EffectTarget source,
        EffectTarget destination,
        SKShader sourceShader)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(sourceShader);
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            throw new ArgumentException("The source must have a materialized target and concrete scale.", nameof(source));
        if (destination.RenderTarget is null || destination.Scale.IsUnbounded)
        {
            throw new ArgumentException(
                "The destination must have a materialized target and concrete scale.",
                nameof(destination));
        }

        return sourceShader.WithLocalMatrix(
            RasterShaderMapping.CreateLocalMatrix(
                destination.Scale.Value,
                source.Scale.Value,
                destination.RasterBounds,
                source.RasterBounds));
    }

    /// <summary>
    /// Supplies a borrowed GPU-backed snapshot shader for a materialized source, mapped into the
    /// destination's backing-buffer coordinates.
    /// </summary>
    /// <remarks>
    /// The shader and its backing image are valid only during <paramref name="use"/>. The callback must
    /// complete every draw that references the shader and must not retain or dispose it.
    /// </remarks>
    public void UseMappedInputShader(
        EffectTarget source,
        EffectTarget destination,
        Action<SKShader> use,
        SKShaderTileMode x = SKShaderTileMode.Decal,
        SKShaderTileMode y = SKShaderTileMode.Decal)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(use);
        if (!Enum.IsDefined(x))
            throw new ArgumentOutOfRangeException(nameof(x), x, "The shader tile mode is invalid.");
        if (!Enum.IsDefined(y))
            throw new ArgumentOutOfRangeException(nameof(y), y, "The shader tile mode is invalid.");
        if (source.RenderTarget is null || source.Scale.IsUnbounded)
            throw new ArgumentException("The source must have a materialized target and concrete scale.", nameof(source));
        if (source.RenderTarget.Value is null)
            throw new ArgumentException("The source target has no backing surface to sample.", nameof(source));

        source.RenderTarget.PrepareForSampling();
        using SKImage image = source.RenderTarget.Value.Snapshot()
            ?? throw new InvalidOperationException("The source surface could not be snapshotted for sampling.");
        using SKShader sourceShader = image.ToShader(x, y);
        using SKShader mappedShader = CreateMappedInputShader(source, destination, sourceShader);
        use(mappedShader);
    }

    private static EffectTarget AllocateTarget(
        Rect bounds,
        float density,
        PixelRect deviceBounds,
        Vector deviceGridOffset)
    {
        using var renderTarget = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        if (renderTarget != null)
        {
            return new EffectTarget(
                renderTarget,
                bounds,
                EffectiveScale.At(density),
                deviceBounds,
                deviceGridOffset);
        }
        else
        {
            // The empty target makes the subsequent Open() throw — log the cause before that happens.
            s_logger.LogWarning(
                "Custom-effect target allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); returning an empty target.",
                deviceBounds.Width, deviceBounds.Height, density, bounds);
            return new EffectTarget();
        }
    }

    /// <summary>
    /// Opens an <see cref="ImmediateCanvas"/> over <paramref name="target"/>'s buffer.
    /// Throws if the target is empty (allocation failed in <see cref="CreateTarget"/>).
    /// </summary>
    public ImmediateCanvas Open(EffectTarget target)
    {
        if (target.RenderTarget == null)
        {
            throw new InvalidOperationException(
                "Cannot Open an empty EffectTarget — its buffer allocation failed (see the preceding " +
                "CreateTarget warning for the size/cause). The effect fails visibly rather than rendering partially.");
        }

        // Prefer the target's concrete Scale (may be clamped below WorkingScale by CreateTarget).
        float density = target.Scale.IsUnbounded ? WorkingScale : target.Scale.Value;
        Rect rasterBounds = target.RasterBounds;
        Vector rasterOriginTranslation = target.RasterOriginTranslation;
        var canvas = new ImmediateCanvas(
            target.RenderTarget,
            density,
            MaxWorkingScale,
            logicalSize: rasterBounds.Size);
        canvas.PushTransform(Matrix.CreateTranslation(
            rasterOriginTranslation.X,
            rasterOriginTranslation.Y));
        return canvas;
    }
}
