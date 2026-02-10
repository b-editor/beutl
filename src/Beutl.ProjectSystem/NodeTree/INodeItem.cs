using Beutl.Extensibility;
using Beutl.Media;

namespace Beutl.NodeTree;

internal interface ISupportSetValueNodeItem
{
    void SetThrough(INodeItem nodeItem);
}

public interface INodeItem : ICoreObject, IHierarchical, INotifyEdited
{
    IPropertyAdapter? Property { get; }

    Type? AssociatedType { get; }

    object? Value { get; }

    public event EventHandler? TopologyChanged;

    void PreEvaluate(EvaluationContext context);

    void Evaluate(EvaluationContext context);

    void PostEvaluate(EvaluationContext context);
}
