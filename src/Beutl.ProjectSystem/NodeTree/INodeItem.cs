using Beutl.Framework;
using Beutl.Media;

namespace Beutl.NodeTree;

public interface INodeItem : ILogicalElement, IAffectsRender
{
    IAbstractProperty? Property { get; }

    Type? AssociatedType { get; }

    object? Value { get; }

    public event EventHandler? NodeTreeInvalidated;

    void Evaluate(EvaluationContext context);

    void NotifyAttachedToNodeTree(NodeTreeSpace nodeTree);

    void NotifyDetachedFromNodeTree(NodeTreeSpace nodeTree);
}

public interface INodeItem<T> : INodeItem
{
    new IAbstractProperty<T>? Property { get; }

    new T? Value { get; }

    IAbstractProperty? INodeItem.Property => Property;

    object? INodeItem.Value => Value;
}
