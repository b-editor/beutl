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

    /// <summary>
    /// The exact device-buffer dimensions <see cref="CreateTarget"/> allocates for a logical
    /// <paramref name="bounds"/> at working density <paramref name="w"/> (feature 003). Shared so a shader's
    /// resolution uniforms (SKSL <c>width</c>/<c>height</c>/<c>iResolution</c>, GLSL <c>Width</c>/<c>Height</c>)
    /// report the SAME size the shader actually iterates — at <c>w == 1</c> the <c>(int)</c>-truncation byte
    /// path, at <c>w != 1</c> the <c>ceil(bounds × w)</c> form. Caller passes the post-clamp density.
    /// </summary>
    internal static (int Width, int Height) DeviceBufferSize(Rect bounds, float w)
    {
        int bw = w == 1f ? (int)bounds.Width : (int)MathF.Ceiling(bounds.Width * w);
        int bh = w == 1f ? (int)bounds.Height : (int)MathF.Ceiling(bounds.Height * w);
        return (bw, bh);
    }

    /// <summary>
    /// The density <see cref="CreateTarget"/> will allocate a buffer for <paramref name="bounds"/> at (feature
    /// 003, FR-037(b)): the working scale after the per-buffer dimension clamp. This is the <b>single canonical
    /// source</b> of that value — a shader/point-blit effect that must compute device-pixel uniforms BEFORE it
    /// holds the created target (SKSL/GLSL build their uniform block up front) MUST call this on the SAME bounds
    /// it passes to <see cref="CreateTarget"/>, rather than re-deriving the clamp inline, so the uniforms can
    /// never drift from the buffer the shader iterates. When the effect already holds the created target, prefer
    /// reading <see cref="EffectTarget.Scale"/>.<see cref="EffectiveScale.Value"/> directly.
    /// </summary>
    public float ResolveTargetDensity(Rect bounds)
        => RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, WorkingScale);

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
        // ResolveTargetDensity is the canonical clamp; shader uniform sites call the SAME method so they
        // can never compute a density different from the buffer this allocates.
        float fit = ResolveTargetDensity(bounds);
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

        (int bw, int bh) = DeviceBufferSize(bounds, w);
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

    /// <summary>
    /// Opens an <see cref="ImmediateCanvas"/> over <paramref name="target"/>'s buffer with the baked base CTM
    /// <c>CreateScale(density)</c>. <paramref name="target"/> MUST be a real allocated target: an empty target
    /// (which <see cref="CreateTarget"/> returns only when the GPU buffer allocation genuinely fails after the
    /// budget clamp already fit the dimensions — i.e. true OOM) throws. The cause is logged at the
    /// <see cref="CreateTarget"/> warning; this is a deliberate fail-visibly path (a partial/wrong effect is not
    /// shipped). A caller that wants to degrade instead should check the returned target before calling Open.
    /// </summary>
    public ImmediateCanvas Open(EffectTarget target)
    {
        if (target.RenderTarget == null)
        {
            throw new InvalidOperationException(
                "Cannot Open an empty EffectTarget — its buffer allocation failed (see the preceding " +
                "CreateTarget warning for the size/cause). The effect fails visibly rather than rendering partially.");
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
