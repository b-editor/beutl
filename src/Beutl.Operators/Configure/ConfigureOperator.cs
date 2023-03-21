using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.Media;
using Beutl.Rendering;
using Beutl.Operation;
using System.Buffers;

namespace Beutl.Operators.Configure;

public abstract class ConfigureOperator<TTarget, TValue> : SourceOperator, ISourceTransformer
    where TTarget : IRenderable
    where TValue : CoreObject, IAffectsRender, new()
{
    private bool _transforming;
    private Renderable[]? _snapshot;
    private int _snapshotCount;

    public ConfigureOperator()
    {
        Value = new TValue();
        Value.Invalidated += (_, e) =>
        {
            if (!_transforming)
            {
                RaiseInvalidated(e);
            }
        };

        Type anmPropType = typeof(AnimatableCorePropertyImpl<>);
        Type propType = typeof(CorePropertyImpl<>);
        Type ownerType = typeof(TTarget);
        bool isAnimatable = Value is IAnimatable;

        Properties.AddRange(GetProperties().Select(x =>
        {
            Type propTypeFact = (isAnimatable && x.GetMetadata<CorePropertyMetadata>(ownerType).PropertyFlags.HasFlag(PropertyFlags.Animatable)
                ? anmPropType
                : propType).MakeGenericType(x.PropertyType);

            return (IAbstractProperty)Activator.CreateInstance(propTypeFact, x, Value)!;
        }));
    }

    protected TValue Value { get; }

    public void Transform(IList<Renderable> value, IClock clock)
    {
        try
        {
            _transforming = true;

            if (_snapshot != null)
            {
                var set = new HashSet<Renderable>(value);

                foreach (Renderable item in _snapshot.AsSpan().Slice(_snapshotCount))
                {
                    if (set.Add(item) && item is TTarget target)
                    {
                        // valueにない、つまり削除された。
                        PreProcess(target, Value);
                        OnDetached(target, Value);
                        PostProcess(target, Value);
                    }
                }

                set.IntersectWith(_snapshot.Take(_snapshotCount));
                foreach (Renderable item in value)
                {
                    if (set.Add(item) && item is TTarget target)
                    {
                        // _snapshotになくてvalueにある。(追加された)
                        PreProcess(target, Value);
                        OnAttached(target, Value);
                        PostProcess(target, Value);
                    }
                }
            }
            else
            {
                foreach (TTarget item in value.OfType<TTarget>())
                {
                    PreProcess(item, Value);
                    OnAttached(item, Value);
                    PostProcess(item, Value);
                }
            }
        }
        finally
        {
            _transforming = false;

            if (_snapshot != null)
            {
                if (_snapshot.Length < value.Count)
                {
                    ArrayPool<Renderable>.Shared.Return(_snapshot, true);
                    _snapshot = ArrayPool<Renderable>.Shared.Rent(value.Count);
                }
            }
            else
            {
                _snapshot = ArrayPool<Renderable>.Shared.Rent(value.Count);
            }

            _snapshotCount = value.Count;
            value.CopyTo(_snapshot, 0);
        }
    }

    public override void UninitializeForContext(OperatorEvaluationContext context)
    {
        base.UninitializeForContext(context);
        if (_snapshot != null)
        {
            foreach (TTarget item in _snapshot.Take(_snapshotCount).OfType<TTarget>())
            {
                OnDetached(item, Value);
            }

            ArrayPool<Renderable>.Shared.Return(_snapshot, true);
            _snapshot = null;
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobj
            && jobj.TryGetPropertyValue("value", out JsonNode? node)
            && node != null)
        {
            Value.ReadFromJson(node);
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobj)
        {
            JsonNode node = new JsonObject();
            Value.WriteToJson(ref node);
            jobj["value"] = node;
        }
    }

    public override void Exit()
    {
        base.Exit();

        if (_snapshot != null)
        {
            foreach (TTarget item in _snapshot.Take(_snapshotCount).OfType<TTarget>())
            {
                OnDetached(item, Value);
            }

            ArrayPool<Renderable>.Shared.Return(_snapshot, true);
            _snapshot = null;
        }
    }

    protected virtual void PreProcess(TTarget target, TValue value)
    {
    }

    protected virtual void PostProcess(TTarget target, TValue value)
    {
    }

    protected abstract void OnAttached(TTarget target, TValue value);

    protected abstract void OnDetached(TTarget target, TValue value);

    protected virtual IEnumerable<CoreProperty> GetProperties()
    {
        yield break;
    }
}
