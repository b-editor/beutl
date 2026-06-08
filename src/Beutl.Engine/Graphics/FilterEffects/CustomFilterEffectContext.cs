using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    internal CustomFilterEffectContext(EffectTargets targets, float outputScale = 1f, float workingScale = 1f)
    {
        Targets = targets;
        OutputScale = outputScale;
        WorkingScale = workingScale;
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
        int bw = w == 1f ? (int)bounds.Width : (int)MathF.Ceiling(bounds.Width * w);
        int bh = w == 1f ? (int)bounds.Height : (int)MathF.Ceiling(bounds.Height * w);
        using var renderTarget = RenderTarget.Create(bw, bh);
        if (renderTarget != null)
        {
            return new EffectTarget(renderTarget, bounds, EffectiveScale.At(w));
        }
        else
        {
            return new EffectTarget();
        }
    }

    public ImmediateCanvas Open(EffectTarget target)
    {
        if (target.RenderTarget == null)
        {
            throw new InvalidOperationException("無効なEffectTarget");
        }

        // feature 003 (CSM3-1): a custom effect renders logical content into this ceil(bounds × w) buffer
        // (pushing CreateScale(w) itself), so tag OutputScale = w. A SourceBackdrop captured here then records
        // its true device density for the replay to un-scale by. w == 1 keeps the default 1 (byte-identical).
        return new ImmediateCanvas(target.RenderTarget, WorkingScale);
    }
}
