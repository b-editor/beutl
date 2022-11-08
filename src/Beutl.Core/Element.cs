namespace Beutl;

public abstract class Element : CoreObject, ILogicalElement
{
    public static readonly CoreProperty<Element?> ParentProperty;
    private ILogicalElement? _parent;

    static Element()
    {
        ParentProperty = ConfigureProperty<Element?, Element>(nameof(Parent))
            .Accessor(o => o.Parent, (o, v) => o.Parent = v)
            .Register();
    }

    public Element? Parent
    {
        get => _parent as Element;
        private set
        {
            var parent = Parent;
            SetAndRaise(ParentProperty, ref parent, value);
            _parent = parent;
        }
    }

    ILogicalElement? ILogicalElement.LogicalParent => _parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => OnEnumerateChildren();

    public event EventHandler<LogicalTreeAttachmentEventArgs>? AttachedToLogicalTree;

    public event EventHandler<LogicalTreeAttachmentEventArgs>? DetachedFromLogicalTree;

    protected static void LogicalChild<T>(
        CoreProperty? property1 = null,
        CoreProperty? property2 = null,
        CoreProperty? property3 = null,
        CoreProperty? property4 = null)
        where T : Element
    {
        static void onNext(CorePropertyChangedEventArgs e)
        {
            if (e.Sender is T s)
            {
                if (e.OldValue is ILogicalElement oldLogical)
                {
                    oldLogical.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }

                if (e.NewValue is ILogicalElement newLogical)
                {
                    newLogical.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }
            }
        }

        property1?.Changed.Subscribe(onNext);
        property2?.Changed.Subscribe(onNext);
        property3?.Changed.Subscribe(onNext);
        property4?.Changed.Subscribe(onNext);
    }

    protected static void LogicalChild<T>(params CoreProperty[] properties)
        where T : Element
    {
        static void onNext(CorePropertyChangedEventArgs e)
        {
            if (e.Sender is T s)
            {
                if (e.OldValue is ILogicalElement oldLogical)
                {
                    oldLogical.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }

                if (e.NewValue is ILogicalElement newLogical)
                {
                    newLogical.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }
            }
        }

        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(onNext);
        }
    }

    protected virtual void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual IEnumerable<ILogicalElement> OnEnumerateChildren()
    {
        yield break;
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        if (_parent is { })
            throw new LogicalTreeException("This logical element already has a parent element.");

        OnAttachedToLogicalTree(e);
        _parent = e.Parent;
        AttachedToLogicalTree?.Invoke(this, e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        if (!ReferenceEquals(e.Parent, _parent))
            throw new LogicalTreeException("The detach source element and the parent element do not match.");

        OnDetachedFromLogicalTree(e);
        _parent = null;
        DetachedFromLogicalTree?.Invoke(this, e);
    }
}
