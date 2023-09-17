using System.Reactive.Linq;

using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.Extensibility;

public interface IAbstractProperty
{
    Type ImplementedType { get; }

    Type PropertyType { get; }

    string DisplayName { get; }

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

internal sealed class KeyFramePropertyWrapper<T> : IAbstractProperty<T>
{
    private readonly KeyFrame<T> _keyFrame;
    private readonly KeyFrameAnimation<T> _animation;

    public KeyFramePropertyWrapper(KeyFrame<T> keyFrame, KeyFrameAnimation<T> animation)
    {
        _keyFrame = keyFrame;
        _animation = animation;
    }

    public Type ImplementedType => _animation.Property.OwnerType;

    public Type PropertyType => _animation.Property.PropertyType;

    public string DisplayName => "KeyFrame Value";

    public bool IsReadOnly => false;

    public IObservable<T?> GetObservable()
    {
        return _keyFrame.GetObservable(GetProperty());
    }

    public T? GetValue()
    {
        return _keyFrame.Value;
    }

    public void SetValue(T? value)
    {
        _keyFrame.SetValue(GetProperty(), value);
    }

    CoreProperty? IAbstractProperty.GetCoreProperty() => _animation.Property;

    private static CoreProperty<T?> GetProperty()
    {
        return KeyFrame<T>.ValueProperty;
    }

    public object? GetDefaultValue()
    {
        return _animation.Property.GetMetadata<ICorePropertyMetadata>(ImplementedType).GetDefaultValue();
    }
}
