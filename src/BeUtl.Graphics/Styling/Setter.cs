using System.Reactive;
using System.Reactive.Linq;

using BeUtl.Animation;
using BeUtl.Collections;
using BeUtl.Media;
using BeUtl.Reactive;

namespace BeUtl.Styling;

public class Setter<T> : LightweightObservableBase<T?>, ISetter
{
    private CoreProperty<T>? _property;
    private T? _value;
    private Animation<T>? _animation;

    public Setter()
    {
    }

    public Setter(CoreProperty<T> property, T? value)
    {
        _property = property;
        Value = value;
    }

    public CoreProperty<T> Property
    {
        get => _property ?? throw new InvalidOperationException();
        set => _property = value;
    }

    public T? Value
    {
        get => _value;
        set
        {
            if (!EqualityComparer<T>.Default.Equals(_value, value))
            {
                var args = new StylingTreeAttachmentEventArgs(this);
                if (_value is IAffectsRender oldValue)
                {
                    oldValue.Invalidated -= Value_Invalidated;
                }
                if (_value is IStylingElement oldElement)
                {
                    oldElement.NotifyDetachedFromStylingTree(args);
                }

                _value = value;
                PublishNext(value);

                Invalidated?.Invoke(this, EventArgs.Empty);
                if (value is IAffectsRender newValue)
                {
                    newValue.Invalidated += Value_Invalidated;
                }
                if (value is IStylingElement newElement)
                {
                    newElement.NotifyAttachedToStylingTree(args);
                }
            }
        }
    }

    public Animation<T>? Animation
    {
        get => _animation;
        set
        {
            if (_animation != value)
            {
                var args = new StylingTreeAttachmentEventArgs(this);
                if (_animation != null)
                {
                    _animation.Invalidated -= Animation_Invalidated;
                    (_animation as IStylingElement).NotifyDetachedFromStylingTree(args);
                }

                _animation = value;

                if (value != null)
                {
                    value.Invalidated += Animation_Invalidated;
                    (value as IStylingElement).NotifyAttachedToStylingTree(args);
                }
            }
        }
    }

    public IStylingElement? StylingParent { get; private set; }

    public IEnumerable<IStylingElement> StylingChildren
    {
        get
        {
            if (Animation != null)
                yield return Animation;
            if (Value is IStylingElement element)
                yield return element;
        }
    }

    CoreProperty ISetter.Property => Property;

    object? ISetter.Value => Value;

    IAnimation? ISetter.Animation => _animation;

    public event EventHandler? Invalidated;

    public event EventHandler<StylingTreeAttachmentEventArgs>? AttachedToStylingTree;

    public event EventHandler<StylingTreeAttachmentEventArgs>? DetachedFromStylingTree;

    public ISetterInstance Instance(IStyleable target)
    {
        return new SetterInstance<T>(this, target);
    }

    public IObservable<Unit> GetObservable()
    {
        return this.Select(i => Unit.Default);
    }

    protected override void Subscribed(IObserver<T?> observer, bool first)
    {
        observer.OnNext(_value);
    }

    protected override void Deinitialize()
    {
    }

    protected override void Initialize()
    {
    }

    private void Value_Invalidated(object? sender, EventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void Animation_Invalidated(object? sender, EventArgs e)
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    void IStylingElement.NotifyAttachedToStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        if (StylingParent is { })
            throw new StylingTreeException("This styling element already has a parent element.");

        StylingParent = e.Parent;
        AttachedToStylingTree?.Invoke(this, e);
    }

    void IStylingElement.NotifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        if (!ReferenceEquals(e.Parent, StylingParent))
            throw new StylingTreeException("The detach source element and the parent element do not match.");

        StylingParent = null;
        DetachedFromStylingTree?.Invoke(this, e);
    }
}
