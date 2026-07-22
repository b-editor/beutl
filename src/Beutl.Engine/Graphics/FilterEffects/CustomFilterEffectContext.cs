using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    private static readonly ILogger s_logger = Log.CreateLogger("CustomFilterEffectContext");

    internal CustomFilterEffectContext(
        EffectTargets targets,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        float outputScale = 1f,
        float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity)
    {
        if (!Enum.IsDefined(intent))
            throw new ArgumentOutOfRangeException(nameof(intent), intent, "The render intent is invalid.");
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "The render request purpose is invalid.");

        Targets = targets;
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
    /// uses the canonical near-edge/far-edge device footprint from <see cref="DeviceBufferBounds"/>.
    /// Absolute-length pixel parameters must be multiplied by this.
    /// </summary>
    public float WorkingScale { get; }

    /// <summary>Working-scale ceiling forwarded into canvases from <see cref="Open"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; }

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
    /// a fractional logical origin. Shared so shader resolution uniforms match <see cref="CreateTarget"/>'s allocation.
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
    /// The density <see cref="CreateTarget"/> will allocate for <paramref name="bounds"/>
    /// (working scale after per-buffer dimension clamp). Call on the same bounds passed to
    /// <see cref="CreateTarget"/> so shader uniforms match the actual buffer.
    /// </summary>
    public float ResolveTargetDensity(Rect bounds)
        => RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(bounds, WorkingScale);

    public EffectTarget CreateTarget(Rect bounds)
    {
        float w = WorkingScale;
        // Re-clamp at allocation site: bounds may exceed what node-level clamps saw.
        float fit = ResolveTargetDensity(bounds);
        if (fit < w)
        {
            s_logger.LogWarning(
                "CreateTarget clamped the working scale {From} -> {To} to keep the buffer within the GPU axis limit (bounds {Bounds}). Use the returned target's Scale for output device math, not context.WorkingScale.",
                w, fit, bounds);
            w = fit;
        }

        PixelRect deviceBounds = DeviceBufferBounds(bounds, w);
        return CreateTargetCore(bounds, w, deviceBounds);
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

    private static EffectTarget CreateTargetCore(Rect bounds, float density, PixelRect deviceBounds)
    {
        using var renderTarget = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        if (renderTarget != null)
        {
            return new EffectTarget(renderTarget, bounds, EffectiveScale.At(density), deviceBounds);
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
        var canvas = new ImmediateCanvas(
            target.RenderTarget,
            density,
            MaxWorkingScale,
            logicalSize: rasterBounds.Size);
        canvas.PushTransform(Matrix.CreateTranslation(
            target.Bounds.X - rasterBounds.X,
            target.Bounds.Y - rasterBounds.Y));
        return canvas;
    }
}
