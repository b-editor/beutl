using System.Collections.Immutable;

using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class FilterEffectCustomOperationContext
{
    private readonly IImmediateCanvasFactory _factory;
    private readonly ImmutableArray<FEItemWrapper> _history;

    internal FilterEffectCustomOperationContext(
        IImmediateCanvasFactory canvas,
        EffectTargets targets,
        ImmutableArray<FEItemWrapper> history)
    {
        Targets = targets;
        _factory = canvas;
        _history = history;
    }

    public EffectTargets Targets { get; }

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
