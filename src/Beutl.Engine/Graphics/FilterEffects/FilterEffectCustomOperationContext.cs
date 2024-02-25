using System.Collections.Immutable;
using System.ComponentModel;

using Beutl.Media.Source;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

[EditorBrowsable(EditorBrowsableState.Advanced)]
[Obsolete("Use CustomFilterEffectContext")]
public class FilterEffectCustomOperationContext
{
    private readonly IImmediateCanvasFactory _factory;
    private readonly ImmutableArray<FEItemWrapper> _history;
    private EffectTarget _target;

    internal FilterEffectCustomOperationContext(
        IImmediateCanvasFactory canvas,
        EffectTarget target,
        ImmutableArray<FEItemWrapper> history)
    {
        _target = target.Clone();
        _factory = canvas;
        _history = history;
    }

    public EffectTarget Target
    {
        get => _target;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _target = value;
        }
    }

    public void ReplaceTarget(EffectTarget target)
    {
        Target.Dispose();
        Target = target.Clone();
    }

    public EffectTarget CreateTarget(int width, int height)
    {
        SKSurface? surface = _factory.CreateRenderTarget(width, height);
        if (surface != null)
        {
            using var surfaceRef = Ref<SKSurface>.Create(surface);
            var obj = new EffectTarget(surfaceRef, new Rect(_target.Bounds.X, _target.Bounds.Y, width, height));

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
