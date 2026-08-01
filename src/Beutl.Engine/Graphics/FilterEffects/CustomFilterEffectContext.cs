using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

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
    /// Gets the nominal working density <c>w</c> requested for this callback. <see cref="CreateTarget"/>
    /// can clamp a specific allocation below this value; call <see cref="ResolveTargetDensity"/> before
    /// allocation or use the returned target's <see cref="EffectTarget.Scale"/> for device-pixel math.
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
    /// The legacy custom-effect contract sizes the local buffer from the logical dimensions only;
    /// a fractional logical origin does not add a rounding pixel.
    /// </summary>
    public static (int Width, int Height) DeviceBufferSize(Rect bounds, float w)
    {
        int width = w == 1f ? (int)bounds.Width : (int)MathF.Ceiling(bounds.Width * w);
        int height = w == 1f ? (int)bounds.Height : (int)MathF.Ceiling(bounds.Height * w);
        return (width, height);
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
    /// after applying the legacy per-buffer dimension clamp.
    /// </summary>
    public float ResolveTargetDensity(Rect bounds)
        => RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            new Rect(default, bounds.Size),
            WorkingScale);

    public EffectTarget CreateTarget(Rect bounds)
        => CreateTargetCore(bounds, WorkingScale);

    private EffectTarget CreateTargetCore(Rect bounds, float requestedDensity)
    {
        float w = requestedDensity;
        // Re-clamp at allocation site: bounds may exceed what node-level clamps saw.
        float fit = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            new Rect(default, bounds.Size),
            w);
        if (fit < w)
        {
            s_logger.LogWarning(
                "CreateTarget clamped the working scale {From} -> {To} to keep the buffer within the GPU axis limit (bounds {Bounds}). Use the returned target's Scale for output device math, not context.WorkingScale.",
                w, fit, bounds);
            w = fit;
        }

        PixelPoint deviceOrigin = DeviceBufferBounds(
            bounds.Translate(_deviceGridOffset),
            w).Position;
        (int width, int height) = DeviceBufferSize(bounds, w);
        var deviceBounds = new PixelRect(
            deviceOrigin,
            new PixelSize(width, height));
        return AllocateTarget(bounds, w, deviceBounds);
    }

    private static EffectTarget AllocateTarget(
        Rect bounds,
        float density,
        PixelRect deviceBounds)
    {
        using var renderTarget = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height);
        if (renderTarget != null)
        {
            Vector legacyGridOffset = deviceBounds
                .ToRect(density)
                .Position - bounds.Position;
            return new EffectTarget(
                renderTarget,
                bounds,
                EffectiveScale.At(density),
                deviceBounds,
                legacyGridOffset,
                preserveLegacyRasterPlacement: true);
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
        return new ImmediateCanvas(
            target.RenderTarget,
            density,
            MaxWorkingScale,
            logicalSize: target.Bounds.Size);
    }
}
