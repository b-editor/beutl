using Beutl.Framework;
using Beutl.Media;

namespace Beutl.NodeTree;

public abstract class NodeItem : Element
{
    static NodeItem()
    {
        IdProperty.OverrideMetadata<NodeItem>(new CorePropertyMetadata<Guid>("id"));
    }

    public NodeItem()
    {
        Id = Guid.NewGuid();
    }

    public event EventHandler? NodeTreeInvalidated;

    protected void InvalidateNodeTree()
    {
        NodeTreeInvalidated?.Invoke(this, EventArgs.Empty);
    }
}

public class NodeItem<T> : NodeItem, INodeItem<T>
{
    private IAbstractProperty<T>? _property;

    // HasAnimationの変更通知を取り消す
    private IDisposable? _disposable;
    private bool _hasAnimation = false;

    public IAbstractProperty<T>? Property
    {
        get => _property;
        protected set
        {
            if (_property != value)
            {
                _disposable?.Dispose();
                _property = value;
                _hasAnimation = false;
                if (value is IAbstractAnimatableProperty<T> animatableProperty)
                {
                    _disposable = animatableProperty.HasAnimation.Subscribe(v => _hasAnimation = v);
                }
            }
        }
    }

    // レンダリング時に変更されるので、変更通知は必要ない
    public T? Value { get; set; }

    public virtual Type? AssociatedType => typeof(T);

    public NodeTreeSpace? NodeTree { get; private set; }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public virtual void Evaluate(EvaluationContext context)
    {
        if (Property is { } property)
        {
            if (_hasAnimation && property is IAbstractAnimatableProperty<T> animatableProperty)
            {
                Value = animatableProperty.Animation.Interpolate(context.Clock.CurrentTime);
            }
            else
            {
                Value = property.GetValue();
            }
        }
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
}
