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
    /// <summary>
    /// Gets or sets the parent element.
    /// </summary>
    public Element? Parent { get; private set; }

    ILogicalElement? ILogicalElement.LogicalParent => Parent;

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
        if (args is CorePropertyChangedEventArgs coreArgs)
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
        OnAttachedToLogicalTree(e);
        Parent = e.Parent as Element;
        AttachedToLogicalTree?.Invoke(this, e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        OnDetachedFromLogicalTree(e);
        Parent = e.Parent as Element;
        DetachedFromLogicalTree?.Invoke(this, e);
    }
}
