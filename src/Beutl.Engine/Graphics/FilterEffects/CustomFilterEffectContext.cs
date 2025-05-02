using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    internal CustomFilterEffectContext(EffectTargets targets)
    {
        Targets = targets;
    }

    public EffectTargets Targets { get; }

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
