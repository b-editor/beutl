using Beutl.Framework;
using Beutl.Media;

namespace Beutl.NodeTree;

internal interface ISupportSetValueNodeItem
{
    void SetThrough(INodeItem nodeItem);
}

public interface INodeItem : ILogicalElement, IAffectsRender
{
    int LocalId { get; }

    string Name { get; set; }

    IAbstractProperty? Property { get; }

    Type? AssociatedType { get; }

    object? Value { get; }

    public event EventHandler? NodeTreeInvalidated;

    void PreEvaluate(EvaluationContext context);

    void Evaluate(EvaluationContext context);

    void PostEvaluate(EvaluationContext context);

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
