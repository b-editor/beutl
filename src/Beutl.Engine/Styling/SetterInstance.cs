using Beutl.Animation;

namespace Beutl.Styling;

#pragma warning disable CA1816

public class SetterInstance<T>(Setter<T> setter, ICoreObject target) : ISetterInstance
{
    private ICoreObject? _target = target;
    private Setter<T>? _setter = setter;

    public CoreProperty<T> Property => Setter.Property;

    public Setter<T> Setter => _setter ?? throw new InvalidOperationException();

    public ICoreObject Target => _target ?? throw new InvalidOperationException();

    CoreProperty ISetterInstance.Property => Property;

    ISetter ISetterInstance.Setter => Setter;

    public void Apply(IClock clock)
    {
        if (Setter.Animation is { } animation
            && Target is Animatable animatable)
        {
            animation.ApplyAnimation(animatable, clock);
        }
        else
        {
            Target.SetValue(Property, Setter.Value);
        }
    }

    public void Begin()
    {
    }

    public void Dispose()
    {
        _target?.ClearValue(Property);
        _setter = null;
        _target = null;
    }

    public void End()
    {
    }
}
