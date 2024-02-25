using System.Collections.Immutable;

using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class CustomFilterEffectContext
{
    internal readonly IImmediateCanvasFactory _factory;
    internal readonly ImmutableArray<FEItemWrapper> _history;

    internal CustomFilterEffectContext(
        IImmediateCanvasFactory canvas,
        EffectTargets targets,
        ImmutableArray<FEItemWrapper> history)
    {
        Targets = targets;
        _factory = canvas;
        _history = history;
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
        SKSurface? surface = _factory.CreateRenderTarget((int)bounds.Width, (int)bounds.Height);
        if (surface != null)
        {
            using var surfaceRef = Ref<SKSurface>.Create(surface);
            var obj = new EffectTarget(surfaceRef, bounds);

            obj._history.AddRange(_history);
            return obj;
        }
        else
        {
            var obj = new EffectTarget();

            obj._history.AddRange(_history);
            return obj;
        }
    }

    public ImmediateCanvas Open(EffectTarget target)
    {
        if (target.Surface == null)
        {
            throw new InvalidOperationException("無効なEffectTarget");
        }

        return _factory.CreateCanvas(target.Surface.Value, true);
    }
}
