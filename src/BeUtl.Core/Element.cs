using System.ComponentModel;

namespace BeUtl;

/// <summary>
/// Provides the base class for all hierarchal elements.
/// </summary>
public interface IElement : ICoreObject, ILogicalElement
{
    /// <summary>
    /// Gets the parent element.
    /// </summary>
    IElement? Parent { get; }
}

/// <summary>
/// Provides the base class for all hierarchal elements.
/// </summary>
public abstract class Element : CoreObject, IElement
{
    public static readonly CoreProperty<Element?> ParentProperty;
    private ILogicalElement? _parent;

    static Element()
    {
        ParentProperty = ConfigureProperty<Element?, Element>(nameof(Parent))
            .Observability(PropertyObservability.DoNotNotifyLogicalTree)
            .Accessor(o => o.Parent, (o, v) => o.Parent = v)
            .Register();
    }

    /// <summary>
    /// Gets or sets the parent element.
    /// </summary>
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

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Array.Empty<ILogicalElement>();

    IElement? IElement.Parent => Parent;

    public event EventHandler<LogicalTreeAttachmentEventArgs>? AttachedToLogicalTree;

    public event EventHandler<LogicalTreeAttachmentEventArgs>? DetachedFromLogicalTree;

    protected virtual void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        if (args is CorePropertyChangedEventArgs coreArgs &&
            coreArgs.PropertyMetadata.Observability != PropertyObservability.DoNotNotifyLogicalTree)
        {
            if (coreArgs.OldValue is ILogicalElement oldLogical)
            {
                oldLogical.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(this));
            }

            if (coreArgs.NewValue is ILogicalElement newLogical)
            {
                newLogical.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(this));
            }
        }

        base.OnPropertyChanged(args);
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
