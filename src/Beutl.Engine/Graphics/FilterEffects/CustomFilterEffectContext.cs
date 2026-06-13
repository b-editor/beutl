using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    private static readonly ILogger s_logger = Log.CreateLogger("CustomFilterEffectContext");

    internal CustomFilterEffectContext(EffectTargets targets, float outputScale = 1f, float workingScale = 1f,
        float maxWorkingScale = float.PositiveInfinity)
    {
        Targets = targets;
        OutputScale = outputScale;
        WorkingScale = workingScale;
        MaxWorkingScale = maxWorkingScale;
    }

    public EffectTargets Targets { get; }

    /// <summary>
    /// The render request's output scale <c>s_out</c> (feature 003, FR-015). The final target only; not a
    /// ceiling on this effect's working scale. Forwarded so a custom effect that re-applies a nested
    /// <see cref="FilterEffectContext"/> keeps the real output scale instead of defaulting to <c>1.0</c>.
    /// </summary>
    public float OutputScale { get; }

    /// <summary>
    /// The working density <c>w</c> this effect's buffers are allocated at (feature 003, FR-009):
    /// <see cref="CreateTarget"/> sizes them <c>ceil(bounds × w)</c> device px. A custom effect MUST
    /// multiply any ABSOLUTE-length pixel parameter (tile size, displacement amount, split offset) by
    /// this so the parameter stays logical; content-relative effects (e.g. a luminance pixel-sort) need
    /// no change. <c>1.0</c> is the pre-feature path (byte-identical).
    /// </summary>
    public float WorkingScale { get; }

    /// <summary>
    /// The render request's working-scale ceiling (feature 003, FR-037), forwarded into the canvases
    /// <see cref="Open"/> returns so nested pulls (drawable brushes, nested drawables) drawn by a custom
    /// effect stay under the request's ceiling. <c>+∞</c> (default) = no ceiling.
    /// </summary>
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

    public EffectTarget CreateTarget(Rect bounds)
    {
        // feature 003: allocate a ceil(bounds × w) device buffer and tag it with its TRUE density At(w) — a
        // custom effect buffer is a concrete bitmap at working density w, including w == 1 (it is not vector
        // / re-rasterizable). The w == 1 size keeps the exact (int)-truncation fast path; the At(1) tag still
        // takes the point-blit branch downstream (Value == 1f), so it stays cheap, but it now reports its
        // density honestly so a consumer caps its working scale at w (no fake upsampling above source detail).
        float w = WorkingScale;
        // FR-037(b) backstop: a custom effect can inflate `bounds` past anything the node-level / flush
        // clamps saw (TransformEffect AABB, path-follow AABB, …), so re-clamp at this third allocation
        // site too — degrading density beats an un-allocatable buffer followed by Open() throwing.
        float fit = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, w);
        if (fit < w)
        {
            // The returned target reports At(fit), and Open() tags its canvas with that same density, so a
            // consumer that derives its working scale from the target it just created (the built-in flatten /
            // transform / path-follow / blend effects do) stays consistent. A consumer that hard-codes
            // context.WorkingScale for output device math would still mismatch this rarer clamped density.
            s_logger.LogWarning(
                "CreateTarget clamped the working scale {From} -> {To} to keep the buffer within the GPU axis limit (bounds {Bounds}). Use the returned target's Scale for output device math, not context.WorkingScale.",
                w, fit, bounds);
            w = fit;
        }

        int bw = w == 1f ? (int)bounds.Width : (int)MathF.Ceiling(bounds.Width * w);
        int bh = w == 1f ? (int)bounds.Height : (int)MathF.Ceiling(bounds.Height * w);
        using var renderTarget = RenderTarget.Create(bw, bh);
        if (renderTarget != null)
        {
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

    public ImmediateCanvas Open(EffectTarget target)
    {
        if (target.RenderTarget == null)
        {
            throw new InvalidOperationException("無効なEffectTarget");
        }

        // feature 003: a custom effect renders LOGICAL content into this ceil(bounds × w) buffer; the canvas
        // bakes the base CTM CreateScale(density) for it (the author no longer pushes CreateScale themselves)
        // and tags the buffer's TRUE density. Prefer the target's own concrete Scale over this context's
        // nominal WorkingScale: CreateTarget may have clamped the density below WorkingScale to keep an
        // inflated buffer allocatable (FR-037(b)), and the canvas's density drives brush fills, nested pulls
        // and backdrop capture on it — they must match the buffer they draw into. In the common (unclamped)
        // case target.Scale.Value == WorkingScale, so this is byte-identical there. Fall back to WorkingScale
        // for an Unbounded target (e.g. a plugin that builds an EffectTarget without setting Scale).
        float density = target.Scale.IsUnbounded ? WorkingScale : target.Scale.Value;
        return new ImmediateCanvas(target.RenderTarget, density, MaxWorkingScale, logicalSize: target.Bounds.Size);
    }
}
