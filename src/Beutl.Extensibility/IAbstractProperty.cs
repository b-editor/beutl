using System.Reactive.Linq;

using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.Extensibility;

public interface IAbstractProperty
{
    Type ImplementedType { get; }

    Type PropertyType { get; }

    string DisplayName { get; }

    string? Description { get; }

    bool IsReadOnly { get; }

    object? GetDefaultValue();

    CoreProperty? GetCoreProperty() => null;

    void SetValue(object? value);

    object? GetValue();

    IObservable<object?> GetObservable();
}

public interface IAbstractProperty<T> : IAbstractProperty
{
    void SetValue(T? value);

    new T? GetValue();

    new IObservable<T?> GetObservable();

    void IAbstractProperty.SetValue(object? value)
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

    object? IAbstractProperty.GetValue()
    {
        return GetValue();
    }

    IObservable<object?> IAbstractProperty.GetObservable()
    {
        return GetObservable().Select(x => (object?)x);
    }
}

public interface IAbstractAnimatableProperty : IAbstractProperty
{
    IAnimation? Animation { get; set; }

    IObservable<IAnimation?> ObserveAnimation { get; }

    internal IAbstractProperty CreateKeyFrameProperty(IKeyFrameAnimation animation, IKeyFrame keyFrame);
}

public interface IAbstractAnimatableProperty<T> : IAbstractProperty<T>, IAbstractAnimatableProperty
{
    new IAnimation<T>? Animation { get; set; }

    new IObservable<IAnimation<T>?> ObserveAnimation { get; }

    IAnimation? IAbstractAnimatableProperty.Animation
    {
        get => Animation;
        set => Animation = value as IAnimation<T>;
    }

    IObservable<IAnimation?> IAbstractAnimatableProperty.ObserveAnimation => ObserveAnimation;

    IAbstractProperty IAbstractAnimatableProperty.CreateKeyFrameProperty(IKeyFrameAnimation animation, IKeyFrame keyFrame)
    {
        return new KeyFramePropertyWrapper<T>((KeyFrame<T>)keyFrame, (KeyFrameAnimation<T>)animation);
    }
}

internal sealed class KeyFramePropertyWrapper<T>(KeyFrame<T> keyFrame, KeyFrameAnimation<T> animation) : IAbstractProperty<T>
{
    public Type ImplementedType => animation.Property.OwnerType;

    public Type PropertyType => animation.Property.PropertyType;

    public string DisplayName => "KeyFrame Value";

    public bool IsReadOnly => false;

    public string? Description => null;

    public IObservable<T?> GetObservable()
    {
        return keyFrame.GetObservable(GetProperty());
    }

    public T? GetValue()
    {
        return keyFrame.Value;
    }

    public void SetValue(T? value)
    {
        keyFrame.SetValue(GetProperty(), value);
    }

    CoreProperty? IAbstractProperty.GetCoreProperty() => animation.Property;

    private static CoreProperty<T?> GetProperty()
    {
        return KeyFrame<T>.ValueProperty;
    }

    public object? GetDefaultValue()
    {
        return animation.Property.GetMetadata<ICorePropertyMetadata>(ImplementedType).GetDefaultValue();
    }
}
