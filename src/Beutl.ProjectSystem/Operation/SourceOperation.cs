using System.Buffers;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Operation;

public sealed class SourceOperation : Hierarchical, INotifyEdited
{
    public static readonly CoreProperty<ICoreList<SourceOperator>> ChildrenProperty;
    private readonly HierarchicalList<SourceOperator> _children;
    private OperatorEvaluationContext[]? _contexts;
    private IRenderer? _lastRenderer;
    private int _contextsLength;
    private bool _isDirty = true;

    static SourceOperation()
    {
        ChildrenProperty = ConfigureProperty<ICoreList<SourceOperator>, SourceOperation>(nameof(Children))
            .Accessor(o => o.Children)
            .Register();
    }

    public SourceOperation()
    {
        _children = new HierarchicalList<SourceOperator>(this);
        _children.Attached += OnOperatorAttached;
        _children.Detached += OnOperatorDetached;
        _children.CollectionChanged += OnOperatorsCollectionChanged;
    }

    public event EventHandler? Edited;

    [NotAutoSerialized]
    public ICoreList<SourceOperator> Children => _children;

    public IRecordableCommand OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        return _children.Select(v => v.OnSplit(backward, startDelta, lengthDelta))
            .Where(v => v != null)
            .ToArray()!
            .ToCommand();
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<SourceOperator[]>(nameof(Children)) is { } children)
        {
            Children.Replace(children);
        }
    }

    public PooledList<EngineObject> Evaluate(EvaluationTarget target, IRenderer renderer, Element element)
    {
        Initialize(renderer);
        var flow = new PooledList<EngineObject>();

        try
        {
            if (_contexts != null)
            {
                foreach (OperatorEvaluationContext? item in _contexts.AsSpan().Slice(0, _contextsLength))
                {
                    EvaluationTarget t = item.Operator.GetEvaluationTarget();
                    if (t == EvaluationTarget.Unknown || t == target)
                    {
                        item.Target = target;
                        item.FlowRenderables = flow;
                        item.Operator.Evaluate(item);
                    }
                }
            }

            return flow;
        }
        catch
        {
            flow.Dispose();
            throw;
        }
    }

    [MemberNotNull(nameof(_contexts))]
    private Memory<OperatorEvaluationContext> RentContextsArray(int size)
    {
        if (_contexts != null)
            throw new InvalidOperationException("ReturnContextsArray has not yet been called.");

        _contexts = ArrayPool<OperatorEvaluationContext>.Shared.Rent(size);
        _contextsLength = size;

        return _contexts.AsMemory().Slice(0, size);
    }

    private void ReturnContextsArray()
    {
        if (_contexts != null)
        {
            ArrayPool<OperatorEvaluationContext>.Shared.Return(_contexts, true);
            _contexts = null;
            _contextsLength = -1;
        }
    }

    private void Uninitialize()
    {
        if (_contexts != null)
        {
            foreach (OperatorEvaluationContext? item in _contexts.AsSpan().Slice(0, _contextsLength))
            {
                item.Operator.UninitializeForContext(item);
            }

            ReturnContextsArray();
        }
    }

    private void Initialize(IRenderer renderer)
    {
        if (_lastRenderer != renderer)
        {
            _lastRenderer = renderer;
            _isDirty = true;
        }

        if (_isDirty)
        {
            Uninitialize();
            Span<OperatorEvaluationContext> contexts = RentContextsArray(Children.Count).Span;

            int index = 0;
            foreach (SourceOperator item in Children.GetMarshal().Value)
            {
                contexts[index++] = new OperatorEvaluationContext(item)
                {
                    Renderer = renderer,
                    List = _contexts
                };
            }

            foreach (OperatorEvaluationContext item in contexts)
            {
                item.Operator.InitializeForContext(item);
            }

            _isDirty = false;
        }
    }

    public IRecordableCommand AddChild(SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        IStorable? storable = this.FindHierarchicalParent<IStorable>();

        return Children.BeginRecord<SourceOperator>()
            .Add(@operator)
            .ToCommand([storable]);
    }

    public IRecordableCommand RemoveChild(SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        IStorable? storable = this.FindHierarchicalParent<IStorable>();

        return Children.BeginRecord<SourceOperator>()
            .Remove(@operator)
            .ToCommand([storable]);
    }

    public IRecordableCommand InsertChild(int index, SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        IStorable? storable = this.FindHierarchicalParent<IStorable>();

        return Children.BeginRecord<SourceOperator>()
            .Insert(index, @operator)
            .ToCommand([storable]);
    }

    private void OnOperatorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _isDirty = true;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private void OnOperatorAttached(SourceOperator obj)
    {
        obj.Edited += OnOperatorEdited;
    }

    private void OnOperatorDetached(SourceOperator obj)
    {
        obj.Edited -= OnOperatorEdited;
    }

    private void OnOperatorEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }
}
