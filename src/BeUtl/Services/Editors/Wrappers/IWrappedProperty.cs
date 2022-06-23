using BeUtl.Animation;

namespace BeUtl.Services.Editors.Wrappers;

public interface IWrappedProperty
{
    object Tag { get; }

    IObservable<string> Header { get; }

    CoreProperty AssociatedProperty { get; }
}

public interface IWrappedProperty<T> : IWrappedProperty
{
    IObservable<T?> GetObservable();

    void SetValue(T value);

    new CoreProperty<T> AssociatedProperty { get; }

    CoreProperty IWrappedProperty.AssociatedProperty => AssociatedProperty;

    public interface IAnimatable : IWrappedProperty<T>
    {
        IObservableList<Animation<T>> Animations { get; }
    }
}
