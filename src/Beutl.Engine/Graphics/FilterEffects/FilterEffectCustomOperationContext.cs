using System.ComponentModel;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

[EditorBrowsable(EditorBrowsableState.Advanced)]
[Obsolete("Use CustomFilterEffectContext")]
public class FilterEffectCustomOperationContext
{
    private EffectTarget _target;

    internal FilterEffectCustomOperationContext(EffectTarget target)
    {
        _target = target.Clone();
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
        using RenderTarget? renderTarget = RenderTarget.Create(width, height);
        if (renderTarget != null)
        {
            return new EffectTarget(renderTarget, new Rect(_target.Bounds.X, _target.Bounds.Y, width, height));
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
