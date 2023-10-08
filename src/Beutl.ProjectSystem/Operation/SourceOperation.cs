using System.Buffers;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Collections.Pooled;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.Serialization;

namespace Beutl.Operation;

public sealed class SourceOperation : Hierarchical, IAffectsRender
{
    public static readonly CoreProperty<ICoreList<SourceOperator>> ChildrenProperty;
    private readonly HierarchicalList<SourceOperator> _children;
    private OperatorEvaluationContext[]? _contexts;
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

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    [NotAutoSerialized]
    public ICoreList<SourceOperator> Children => _children;

    public IRecordableCommand OnSplit(bool backward, TimeSpan startDelta,TimeSpan lengthDelta)
    {
        return _children.Select(v => v.OnSplit(backward, startDelta, lengthDelta))
            .Where(v => v != null)
            .ToArray()!
            .ToCommand();
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);

        if (json.TryGetPropertyValue(nameof(Children), out JsonNode? childrenNode)
            && childrenNode is JsonArray childrenArray)
        {
            foreach (JsonObject operatorJson in childrenArray.OfType<JsonObject>())
            {
                Type? type = operatorJson.GetDiscriminator();
                SourceOperator? @operator = null;
                if (type?.IsAssignableTo(typeof(SourceOperator)) ?? false)
                {
                    @operator = Activator.CreateInstance(type) as SourceOperator;
                }

                @operator ??= new DummySourceOperator();
                @operator.ReadFromJson(operatorJson);
                Children.Add(@operator);
            }
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        Span<SourceOperator> children = _children.GetMarshal().Value;
        if (children.Length > 0)
        {
            var array = new JsonArray();

            foreach (SourceOperator item in children)
            {
                var itemJson = new JsonObject();
                item.WriteToJson(itemJson);

                // DummySourceOperatorはReadFromJsonで取得した、Jsonをリレーするので型名は書かない。
                if (item is not DummySourceOperator)
                    itemJson.WriteDiscriminator(item.GetType());

                array.Add(itemJson);
            }

            json[nameof(Children)] = array;
        }
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

    public PooledList<Renderable> Evaluate(EvaluationTarget target, IRenderer renderer, Element element)
    {
        Initialize(renderer, element.Clock);
        var flow = new PooledList<Renderable>();

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


                foreach (Renderable item in flow.Span)
                {
                    item.ZIndex = element.ZIndex;
                    item.TimeRange = new TimeRange(element.Start, element.Length);
                    item.ApplyStyling(element.Clock);
                    item.ApplyAnimations(element.Clock);
                    item.IsVisible = element.IsEnabled;

                    while (item.BatchUpdate)
                    {
                        item.EndBatchUpdate();
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

    private void Initialize(IRenderer renderer, IClock clock)
    {
        if (_isDirty)
        {
            Uninitialize();
            Span<OperatorEvaluationContext> contexts = RentContextsArray(Children.Count).Span;

            int index = 0;
            foreach (SourceOperator item in Children.GetMarshal().Value)
            {
                contexts[index++] = new OperatorEvaluationContext(item)
                {
                    Clock = clock,
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

        return Children.BeginRecord<SourceOperator>()
            .Add(@operator)
            .ToCommand();
    }

    public IRecordableCommand RemoveChild(SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        return Children.BeginRecord<SourceOperator>()
            .Remove(@operator)
            .ToCommand();
    }

    public IRecordableCommand InsertChild(int index, SourceOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);

        return Children.BeginRecord<SourceOperator>()
            .Insert(index, @operator)
            .ToCommand();
    }

    private void OnOperatorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _isDirty = true;
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this));
    }

    private void OnOperatorAttached(SourceOperator obj)
    {
        obj.Invalidated += OnOperatorInvalidated;
    }

    private void OnOperatorDetached(SourceOperator obj)
    {
        obj.Invalidated -= OnOperatorInvalidated;
    }

    private void OnOperatorInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }
}
