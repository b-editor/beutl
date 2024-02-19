using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

public class FilterEffectCustomOperationContext
{
    private readonly ImmediateCanvas _canvas;

    public FilterEffectCustomOperationContext(ImmediateCanvas canvas, EffectTargets targets)
    {
        Targets = targets;
        _canvas = canvas;
    }

    public EffectTargets Targets { get; }

    public EffectTarget CreateTarget(Rect bounds)
    {
        SKSurface? surface = _canvas.CreateRenderTarget((int)bounds.Width, (int)bounds.Height);
        if (surface != null)
        {
            using var surfaceRef = Ref<SKSurface>.Create(surface);
            return new EffectTarget(surfaceRef, bounds);
        }
        else
        {
            return EffectTarget.Empty;
        }
    }

    public ImmediateCanvas Open(EffectTarget target)
    {
        if (target.Surface == null)
        {
            throw new InvalidOperationException("無効なEffectTarget");
        }

        return _canvas.CreateCanvas(target.Surface.Value, true);
    }
}
