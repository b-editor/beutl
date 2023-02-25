using System.Reactive.Linq;

using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.Framework;

public interface IAbstractProperty
{
    Type ImplementedType { get; }

    CoreProperty Property { get; }

    void SetValue(object? value);

    object? GetValue();

    IObservable<object?> GetObservable();
}

public interface IAbstractProperty<T> : IAbstractProperty
{
    new CoreProperty<T> Property { get; }

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

    CoreProperty IAbstractProperty.Property => Property;
}

public interface IAbstractAnimatableProperty : IAbstractProperty
{
    IAnimation? Animation { get; }

    IObservable<IAnimation?> ObserveAnimation { get; }

    internal IAbstractProperty CreateKeyFrameProperty(IKeyFrameAnimation animation, IKeyFrame keyFrame);
}

public interface IAbstractAnimatableProperty<T> : IAbstractProperty<T>, IAbstractAnimatableProperty
{
    new IAnimation<T>? Animation { get; }

    new IObservable<IAnimation<T>?> ObserveAnimation { get; }

    IAnimation? IAbstractAnimatableProperty.Animation => Animation;

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

    public CoreProperty<T> Property => _animation.Property;

    public Type ImplementedType => Property.OwnerType;

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

    private CoreProperty<T> GetProperty()
    {
        return KeyFrame<T>.ValueProperty;
    }
}
