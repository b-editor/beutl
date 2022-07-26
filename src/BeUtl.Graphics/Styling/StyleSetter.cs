using System.Reactive;
using System.Reactive.Linq;

using BeUtl.Animation;
using BeUtl.Reactive;

namespace BeUtl.Styling;

public class StyleSetter<T> : LightweightObservableBase<Style?>, ISetter
{
    private CoreProperty<T>? _property;
    private Style? _value;

    public StyleSetter()
    {
    }

    public StyleSetter(CoreProperty<T> property, Style? value)
    {
        _property = property;
        Value = value;
    }

    public CoreProperty<T> Property
    {
        get => _property ?? throw new InvalidOperationException();
        set => _property = value;
    }

    public Style? Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                var args = new StylingTreeAttachmentEventArgs(this);
                if (_value != null)
                {
                    _value.Invalidated -= OnInvalidated;
                    (_value as IStylingElement).NotifyDetachedFromStylingTree(args);
                }

                _value = value;
                PublishNext(value);

                Invalidated?.Invoke(this, EventArgs.Empty);

                if (value != null)
                {
                    value.Invalidated += OnInvalidated;
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
            if (_value is { })
                yield return _value;
        }
    }

    CoreProperty ISetter.Property => Property;

    object? ISetter.Value => Value;

    IAnimation? ISetter.Animation => throw new InvalidOperationException();

    public event EventHandler? Invalidated;

    public event EventHandler<StylingTreeAttachmentEventArgs>? AttachedToStylingTree;

    public event EventHandler<StylingTreeAttachmentEventArgs>? DetachedFromStylingTree;

    public ISetterInstance Instance(IStyleable target)
    {
        if (Value?.TargetType?.IsAssignableTo(typeof(T)) == false)
        {
            throw new InvalidCastException($"Unable to cast object of type {Value?.TargetType} to type {typeof(T)}.");
        }
        return new StyleSetterInstance<T>(this, target);
    }

    public IObservable<Unit> GetObservable()
    {
        return this.Select(i => Unit.Default);
    }

    protected override void Subscribed(IObserver<Style?> observer, bool first)
    {
        observer.OnNext(_value);
    }

    protected override void Initialize()
    {
    }

    protected override void Deinitialize()
    {
    }

    private void OnInvalidated(object? sender, EventArgs e)
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
