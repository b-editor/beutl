using BeUtl.Animation;
using BeUtl.Styling;

namespace BeUtl.Services.Editors.Wrappers;

public sealed class StylingSetterWrapper<T> : IWrappedProperty<T>.IAnimatable
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

    public IObservableList<Animation<T>> Animations => ((Setter<T>)Tag).Animations;

    public IObservable<T?> GetObservable()
    {
        return (Setter<T>)Tag;
    }

    public void SetValue(T value)
    {
        ((Setter<T>)Tag).Value = value;
    }
}
