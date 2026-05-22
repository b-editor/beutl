using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    internal CustomFilterEffectContext(EffectTargets targets, RenderScale correctionScale)
    {
        Targets = targets;
        CorrectionScale = correctionScale;
    }

    public EffectTargets Targets { get; }

    /// <summary>
    /// The upstream raster's scale ratio. <see cref="RenderScale.Identity"/> when no per-clip proxy is active.
    /// Custom-effect implementations that build their own <c>SKImageFilter</c> / shader must divide
    /// length-typed authored parameters by this value before invoking Skia (or use
    /// <see cref="RenderScale.ToRaster(Beutl.Graphics.Size)"/> / <see cref="RenderScale.ToRaster(Beutl.Graphics.Point)"/>).
    /// </summary>
    public RenderScale CorrectionScale { get; }

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
        using var renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height);
        if (renderTarget != null)
        {
            return new EffectTarget(renderTarget, bounds);
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

        return new ImmediateCanvas(target.RenderTarget);
    }
}
