using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Collections;

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

    /// <summary>
    /// Gets the children.
    /// </summary>
    IElementList Children { get; }
}

/// <summary>
/// Provides the base class for all hierarchal elements.
/// </summary>
public abstract class Element : CoreObject, IElement
{
    private readonly ElementList _children;

    /// <summary>
    /// Initializes a new instance of the <see cref="Element"/> class.
    /// </summary>
    protected Element()
    {
        _children = new(this);
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Gets or sets the parent element.
    /// </summary>
    public Element? Parent { get; private set; }

    /// <summary>
    /// Gets the children.
    /// </summary>
    public IElementList Children => _children;

    ILogicalElement? ILogicalElement.LogicalParent => Parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Children;

    IElement? IElement.Parent => Parent;

    public event EventHandler<LogicalTreeAttachmentEventArgs>? AttachedToLogicalTree;

    public event EventHandler<LogicalTreeAttachmentEventArgs>? DetachedFromLogicalTree;

    protected virtual void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        Parent = e.NewParent as Element;
        OnAttachedToLogicalTree(e);
        AttachedToLogicalTree?.Invoke(this, e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        Parent = e.NewParent as Element;
        OnDetachedFromLogicalTree(e);
        DetachedFromLogicalTree?.Invoke(this, e);
    }
}
