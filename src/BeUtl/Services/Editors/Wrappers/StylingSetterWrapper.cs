using BeUtl.Animation;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Services.Editors.Wrappers;

public interface IStylingSetterWrapper : IWrappedProperty
{
}

public sealed class StylingSetterWrapper<T> : IWrappedProperty<T>.IAnimatable, IStylingSetterWrapper
{
    public StylingSetterWrapper(Setter<T> setter)
    {
        AssociatedProperty = setter.Property;
        Tag = setter;

        Header = Observable.Return(setter.Property.Name);
    }

    public CoreProperty<T> AssociatedProperty { get; }

    public object Tag { get; }

    public IObservable<string> Header { get; }

    public Animation<T> Animation
    {
        get
        {
            var setter = (Setter<T>)Tag;
            setter.Animation ??= new Animation<T>(AssociatedProperty);
            return setter.Animation;
        }
    }

    public bool HasAnimation
    {
        get
        {
            var setter = (Setter<T>)Tag;
            return setter.Animation is { Children.Count: > 0 };
        }
    }

    public IObservable<T?> GetObservable()
    {
        return (Setter<T>)Tag;
    }

    public T? GetValue()
    {
        return ((Setter<T>)Tag).Value;
    }

    public void SetValue(T? value)
    {
        ((Setter<T>)Tag).Value = value;
    }
}
