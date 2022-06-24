using BeUtl.Animation;

namespace BeUtl.Services.Editors.Wrappers;

public interface IWrappedProperty
{
    object Tag { get; }

    IObservable<string> Header { get; }

    CoreProperty AssociatedProperty { get; }

    void SetValue(object? value);

    object? GetValue();

    public interface IAnimatable : IWrappedProperty
    {
        IReadOnlyList<IAnimation> Animations { get; }
    }
}

public interface IWrappedProperty<T> : IWrappedProperty
{
    IObservable<T?> GetObservable();

    void SetValue(T? value);

    new T? GetValue();

    new CoreProperty<T> AssociatedProperty { get; }

    void IWrappedProperty.SetValue(object? value)
    {
        if (value is T typed)
        {
            SetValue(typed);
        }
        else
        {
            SetValue(default);
        }
    }

    object? IWrappedProperty.GetValue()
    {
        return GetValue();
    }

    CoreProperty IWrappedProperty.AssociatedProperty => AssociatedProperty;

    public new interface IAnimatable : IWrappedProperty<T>, IWrappedProperty.IAnimatable
    {
        new IObservableList<Animation<T>> Animations { get; }
    }
}
