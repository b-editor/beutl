using System.Reactive.Linq;

using Beutl.Animation;

namespace Beutl.Extensibility;

public interface IPropertyAdapter
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

public interface IPropertyAdapter<T> : IPropertyAdapter
{
    void SetValue(T? value);

    new T? GetValue();

    new IObservable<T?> GetObservable();

    void IPropertyAdapter.SetValue(object? value)
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

    object? IPropertyAdapter.GetValue()
    {
        return GetValue();
    }

    IObservable<object?> IPropertyAdapter.GetObservable()
    {
        return GetObservable().Select(x => (object?)x);
    }
}

public interface IAnimatablePropertyAdapter : IPropertyAdapter
{
    IAnimation? Animation { get; set; }

    IObservable<IAnimation?> ObserveAnimation { get; }

    internal IPropertyAdapter CreateKeyFrameProperty(IKeyFrameAnimation animation, IKeyFrame keyFrame);
}

public interface IAnimatablePropertyAdapter<T> : IPropertyAdapter<T>, IAnimatablePropertyAdapter
{
    new IAnimation<T>? Animation { get; set; }

    new IObservable<IAnimation<T>?> ObserveAnimation { get; }

    IAnimation? IAnimatablePropertyAdapter.Animation
    {
        get => Animation;
        set => Animation = value as IAnimation<T>;
    }

    IObservable<IAnimation?> IAnimatablePropertyAdapter.ObserveAnimation => ObserveAnimation;

    IPropertyAdapter IAnimatablePropertyAdapter.CreateKeyFrameProperty(IKeyFrameAnimation animation, IKeyFrame keyFrame)
    {
        return new KeyFramePropertyAdapter<T>((KeyFrame<T>)keyFrame, (KeyFrameAnimation<T>)animation);
    }
}

internal sealed class KeyFramePropertyAdapter<T>(KeyFrame<T> keyFrame, KeyFrameAnimation<T> animation) : IPropertyAdapter<T>
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

    CoreProperty? IPropertyAdapter.GetCoreProperty() => animation.Property;

    private static CoreProperty<T?> GetProperty()
    {
        return KeyFrame<T>.ValueProperty;
    }

    public object? GetDefaultValue()
    {
        return animation.Property.GetMetadata<ICorePropertyMetadata>(ImplementedType).GetDefaultValue();
    }
}
