using BeUtl.Animation;

namespace BeUtl.Services.Editors.Wrappers;

public sealed class AnimationSpanPropertyWrapper<T> : IWrappedProperty<T>
{
    private readonly AnimationSpan<T> _animationSpan;
    private readonly Animation<T> _animation;
    private readonly bool _previous;

    public AnimationSpanPropertyWrapper(AnimationSpan<T> animationSpan, Animation<T> animation, bool previous)
    {
        _animationSpan = animationSpan;
        _animation = animation;
        _previous = previous;
        Header = Observable.Return(previous ? "Previous" : "Next");
    }

    public CoreProperty<T> AssociatedProperty => _animation.Property;

    public object Tag => _animationSpan;

    public IObservable<string> Header { get; }

    public IObservable<T?> GetObservable()
    {
        return _animationSpan.GetObservable(GetProperty());
    }

    public T? GetValue()
    {
        return _previous ? _animationSpan.Previous : _animationSpan.Next;
    }

    public void SetValue(T? value)
    {
        _animationSpan.SetValue(GetProperty(), value);
    }

    private CoreProperty<T> GetProperty()
    {
        return _previous
            ? AnimationSpan<T>.PreviousProperty
            : AnimationSpan<T>.NextProperty;
    }
}
