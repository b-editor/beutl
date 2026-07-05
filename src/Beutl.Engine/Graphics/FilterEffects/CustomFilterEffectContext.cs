using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    private static readonly ILogger s_logger = Log.CreateLogger("CustomFilterEffectContext");

    internal CustomFilterEffectContext(EffectTargets targets, float outputScale = 1f, float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity, PipelineDiagnostics? diagnostics = null)
    {
        Targets = targets;
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = RenderNodeContext.SanitizeMaxWorkingScale(maxWorkingScale);
        Diagnostics = diagnostics;
    }

    public EffectTargets Targets { get; }

    /// <summary>Effect-pipeline counters, or <see langword="null"/> when the render is not observed.</summary>
    public PipelineDiagnostics? Diagnostics { get; }

    /// <summary>The render request's output scale <c>s_out</c>, not a ceiling on this effect's working scale.</summary>
    public float OutputScale { get; }

    /// <summary>
    /// The working density <c>w</c> this effect's buffers are allocated at: <see cref="CreateTarget"/>
    /// sizes them <c>ceil(bounds * w)</c>. Absolute-length pixel parameters must be multiplied by this.
    /// </summary>
    public float WorkingScale { get; }

    /// <summary>Working-scale ceiling forwarded into canvases from <see cref="Open"/>. <c>+Inf</c> = no ceiling.</summary>
    public float MaxWorkingScale { get; }

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
    /// Shared so shader resolution uniforms match <see cref="CreateTarget"/>'s allocation.
    /// </summary>
    public static (int Width, int Height) DeviceBufferSize(Rect bounds, float w)
    {
        int bw = w == 1f ? (int)bounds.Width : (int)MathF.Ceiling(bounds.Width * w);
        int bh = w == 1f ? (int)bounds.Height : (int)MathF.Ceiling(bounds.Height * w);
        return (bw, bh);
    }

    /// <summary>
    /// The density <see cref="CreateTarget"/> will allocate for <paramref name="bounds"/>
    /// (working scale after per-buffer dimension clamp). Call on the same bounds passed to
    /// <see cref="CreateTarget"/> so shader uniforms match the actual buffer.
    /// </summary>
    public float ResolveTargetDensity(Rect bounds)
        => RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, WorkingScale);

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

        (int bw, int bh) = DeviceBufferSize(bounds, w);
        using var renderTarget = RenderTarget.Create(bw, bh);
        if (renderTarget != null)
        {
            if (Diagnostics is { } diag)
                diag.TargetAllocations++;
            return new EffectTarget(renderTarget, bounds, EffectiveScale.At(w));
        }
        else
        {
            // The empty target makes the subsequent Open() throw — log the cause before that happens.
            s_logger.LogWarning(
                "Custom-effect target allocation failed ({Width}x{Height} px, w {WorkingScale}, bounds {Bounds}); returning an empty target.",
                bw, bh, w, bounds);
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

        if (Diagnostics is { } diag)
        {
            diag.GpuPasses++;
            diag.FlushSyncs++;
        }

        // Prefer the target's concrete Scale (may be clamped below WorkingScale by CreateTarget).
        float density = target.Scale.IsUnbounded ? WorkingScale : target.Scale.Value;
        return new ImmediateCanvas(target.RenderTarget, density, MaxWorkingScale, logicalSize: target.Bounds.Size);
    }
}
