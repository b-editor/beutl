using Beutl.Animation;
using Beutl.Framework;
using Beutl.Media;

namespace Beutl.NodeTree;

public abstract class NodeItem : Hierarchical
{
    public static readonly CoreProperty<bool?> IsValidProperty;
    public static readonly CoreProperty<int> LocalIdProperty;
    private bool? _isValid;
    private int _localId = -1;

    static NodeItem()
    {
        IsValidProperty = ConfigureProperty<bool?, NodeItem>(o => o.IsValid)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();

        LocalIdProperty = ConfigureProperty<int, NodeItem>(o => o.LocalId)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .SerializeName("local-id")
            .DefaultValue(-1)
            .Register();

        IdProperty.OverrideMetadata<NodeItem>(new CorePropertyMetadata<Guid>("id"));
    }

    public NodeItem()
    {
        Id = Guid.NewGuid();
    }

    public bool? IsValid
    {
        get => _isValid;
        protected set => SetAndRaise(IsValidProperty, ref _isValid, value);
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
    private IAbstractProperty<T>? _property;

    public IAbstractProperty<T>? Property
    {
        get => _property;
        protected set => _property = value;
    }

    // レンダリング時に変更されるので、変更通知は必要ない
    public T? Value { get; set; }

    public virtual Type? AssociatedType => typeof(T);

    public NodeTreeSpace? NodeTree { get; private set; }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public virtual void PreEvaluate(EvaluationContext context)
    {
        if (Property is { } property)
        {
            if (property is IAbstractAnimatableProperty<T> { Animation: IAnimation<T> animation })
            {
                Value = animation.Interpolate(context.Clock.CurrentTime);
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

    protected virtual void OnAttachedToNodeTree(NodeTreeSpace nodeTree)
    {
    }

    protected virtual void OnDetachedFromNodeTree(NodeTreeSpace nodeTree)
    {
    }

    void INodeItem.NotifyAttachedToNodeTree(NodeTreeSpace nodeTree)
    {
        if (NodeTree != null)
            throw new InvalidOperationException("Already attached to the node tree.");

        NodeTree = nodeTree;
        OnAttachedToNodeTree(nodeTree);
    }

    void INodeItem.NotifyDetachedFromNodeTree(NodeTreeSpace nodeTree)
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
