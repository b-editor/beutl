using Beutl.Animation;
using Beutl.Graphics;

namespace Beutl.Media;

public abstract class PathSegment : Animatable, IAffectsRender
{
    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    protected PathSegment()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public abstract void ApplyTo(IGeometryContext context);

    public bool TryGetEndPoint(out Point point)
    {
        point = GetValue(GetEndPointProperty());
        return true;
    }

    public Point GetEndPoint()
    {
        return GetValue(GetEndPointProperty());
    }
    
    public Point GetEndPoint(TimeSpan localTime, TimeSpan globalTime)
    {
        CoreProperty<Point> prop = GetEndPointProperty();
        if (Animations.FirstOrDefault(a => a.Property == prop) is KeyFrameAnimation<Point> anm)
        {
            if (anm.UseGlobalClock)
            {
                return anm.Interpolate(globalTime);
            }
            else
            {
                return anm.Interpolate(localTime);
            }
        }

        return GetValue(GetEndPointProperty());
    }

    public abstract CoreProperty<Point> GetEndPointProperty();

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : PathSegment
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
