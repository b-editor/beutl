using System.Reactive.Linq;

using BeUtl.Animation;
using BeUtl.Animation.Easings;

namespace BeUtl.Framework;

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
    IAnimation Animation { get; }

    IObservable<bool> HasAnimation { get; }

    IAnimationSpan CreateSpan(Easing easing);

    internal (IAbstractProperty Previous, IAbstractProperty Next) CreateSpanWrapper(IAnimationSpan animationSpan);
}

public interface IAbstractAnimatableProperty<T> : IAbstractProperty<T>, IAbstractAnimatableProperty
{
    new Animation<T> Animation { get; }

    IAnimation IAbstractAnimatableProperty.Animation => Animation;

    IAnimationSpan IAbstractAnimatableProperty.CreateSpan(Easing easing)
    {
        CoreProperty<T> property = Property;
        Type ownerType = property.OwnerType;
        T? defaultValue = GetValue();
        bool hasDefaultValue = true;
        if (defaultValue == null)
        {
            // メタデータをOverrideしている可能性があるので、owner.GetType()をする必要がある。
            CorePropertyMetadata<T> metadata = property.GetMetadata<CorePropertyMetadata<T>>(ownerType);
            defaultValue = metadata.DefaultValue;
            hasDefaultValue = metadata.HasDefaultValue;
        }

        var span = new AnimationSpan<T>
        {
            Easing = easing,
            Duration = TimeSpan.FromSeconds(2)
        };

        if (hasDefaultValue && defaultValue != null)
        {
            span.Previous = defaultValue;
            span.Next = defaultValue;
        }

        return span;
    }

    (IAbstractProperty Previous, IAbstractProperty Next) IAbstractAnimatableProperty.CreateSpanWrapper(IAnimationSpan animationSpan)
    {
        return (new AnimationSpanPropertyWrapper<T>((AnimationSpan<T>)animationSpan, Animation, true),
            new AnimationSpanPropertyWrapper<T>((AnimationSpan<T>)animationSpan, Animation, false));
    }
}

internal sealed class AnimationSpanPropertyWrapper<T> : IAbstractProperty<T>
{
    private readonly AnimationSpan<T> _animationSpan;
    private readonly Animation<T> _animation;

    public AnimationSpanPropertyWrapper(AnimationSpan<T> animationSpan, Animation<T> animation, bool previous)
    {
        _animationSpan = animationSpan;
        _animation = animation;
        Previous = previous;
    }

    public CoreProperty<T> Property => _animation.Property;

    public Type ImplementedType => Property.OwnerType;

    public bool Previous { get; }

    public IObservable<T?> GetObservable()
    {
        return _animationSpan.GetObservable(GetProperty());
    }

    public T? GetValue()
    {
        return Previous ? _animationSpan.Previous : _animationSpan.Next;
    }

    public void SetValue(T? value)
    {
        _animationSpan.SetValue(GetProperty(), value);
    }

    private CoreProperty<T> GetProperty()
    {
        return Previous
            ? AnimationSpan<T>.PreviousProperty
            : AnimationSpan<T>.NextProperty;
    }
}
