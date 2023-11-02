using Beutl.Animation;
using Beutl.Extensibility;
using Beutl.Media;

namespace Beutl.NodeTree;

public abstract class NodeItem : Hierarchical
{
    public static readonly CoreProperty<int> LocalIdProperty;
    private int _localId = -1;

    static NodeItem()
    {
        LocalIdProperty = ConfigureProperty<int, NodeItem>(o => o.LocalId)
            .DefaultValue(-1)
            .Register();
    }

    public int LocalId
    {
        get => _localId;
        set => SetAndRaise(LocalIdProperty, ref _localId, value);
    }

    public event EventHandler? NodeTreeInvalidated;

    protected void InvalidateNodeTree()
    {
        NodeTreeInvalidated?.Invoke(this, EventArgs.Empty);
    }
}

public class NodeItem<T> : NodeItem, INodeItem, ISupportSetValueNodeItem
{
    public IAbstractProperty<T>? Property { get; protected set; }

    // レンダリング時に変更されるので、変更通知は必要ない
    public T? Value { get; set; }

    public virtual Type? AssociatedType => typeof(T);

    public NodeTreeModel? NodeTree { get; private set; }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public virtual void PreEvaluate(EvaluationContext context)
    {
        if (Property is { } property)
        {
            if (property is IAbstractAnimatableProperty<T> { Animation: IAnimation<T> animation })
            {
                Value = animation.GetAnimatedValue(context.Clock);
            }
            else
            {
                Value = property.GetValue();
            }
        }
    }

    public virtual void Evaluate(EvaluationContext context)
    {
    }

    public virtual void PostEvaluate(EvaluationContext context)
    {
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }

    protected virtual void OnAttachedToNodeTree(NodeTreeModel nodeTree)
    {
    }

    protected virtual void OnDetachedFromNodeTree(NodeTreeModel nodeTree)
    {
    }

    void INodeItem.NotifyAttachedToNodeTree(NodeTreeModel nodeTree)
    {
        if (NodeTree != null)
            throw new InvalidOperationException("Already attached to the node tree.");

        NodeTree = nodeTree;
        OnAttachedToNodeTree(nodeTree);
    }

    void INodeItem.NotifyDetachedFromNodeTree(NodeTreeModel nodeTree)
    {
        if (NodeTree == null)
            throw new InvalidOperationException("Already detached from the node tree.");

        NodeTree = null;
        OnDetachedFromNodeTree(nodeTree);
    }

    IAbstractProperty? INodeItem.Property => Property;

    object? INodeItem.Value => Value;

    void ISupportSetValueNodeItem.SetThrough(INodeItem nodeItem)
    {
        if (nodeItem is NodeItem<T> t)
        {
            Value = t.Value;
        }
    }
}
