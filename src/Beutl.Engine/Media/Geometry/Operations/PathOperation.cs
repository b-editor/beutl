using Beutl.Animation;
using Beutl.Graphics;

namespace Beutl.Media;

public abstract class PathOperation : Animatable, IAffectsRender
{
    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public abstract void ApplyTo(IGeometryContext context);

    public abstract bool TryGetEndPoint(out Point point);

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : PathOperation
    {
        foreach (CoreProperty item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.Invalidated?.Invoke(s, new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                    {
                        oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;
                    }

                    if (e.NewValue is IAffectsRender newAffectsRender)
                    {
                        newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
                    }
                }
            });
        }
    }

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
