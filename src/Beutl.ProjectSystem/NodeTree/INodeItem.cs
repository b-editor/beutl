using Beutl.Extensibility;
using Beutl.Media;

namespace Beutl.NodeTree;

internal interface ISupportSetValueNodeItem
{
    void SetThrough(INodeItem nodeItem);
}

public interface INodeItem : ICoreObject, IHierarchical, IAffectsRender
{
    int LocalId { get; }

    IAbstractProperty? Property { get; }

    Type? AssociatedType { get; }

    object? Value { get; }

    public event EventHandler? NodeTreeInvalidated;

    void PreEvaluate(EvaluationContext context);

    void Evaluate(EvaluationContext context);

    void PostEvaluate(EvaluationContext context);

    void NotifyAttachedToNodeTree(NodeTreeModel nodeTree);

    void NotifyDetachedFromNodeTree(NodeTreeModel nodeTree);
}
