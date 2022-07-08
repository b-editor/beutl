using BeUtl.Animation;

namespace BeUtl.Services.Editors.Wrappers;

public interface IWrappedProperty
{
    object Tag { get; }

    IObservable<string> Header { get; }

    CoreProperty AssociatedProperty { get; }

    void SetValue(object? value);

    object? GetValue();

    IObservable<object?> GetObservable();

    public interface IAnimatable : IWrappedProperty
    {
        IReadOnlyList<IAnimationSpan> Animations { get; }

        void AddAnimation(IAnimationSpan animation);

        void RemoveAnimation(IAnimationSpan animation);

        void InsertAnimation(int index, IAnimationSpan animation);
    }
}

public interface IWrappedProperty<T> : IWrappedProperty
{
    new IObservable<T?> GetObservable();

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

    IObservable<object?> IWrappedProperty.GetObservable()
    {
        return GetObservable().Select(x => (object?)x);
    }

    CoreProperty IWrappedProperty.AssociatedProperty => AssociatedProperty;

    public new interface IAnimatable : IWrappedProperty<T>, IWrappedProperty.IAnimatable
    {
        new IObservableList<AnimationSpan<T>> Animations { get; }
    }
}
