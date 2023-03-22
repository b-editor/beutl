using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Framework;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Rendering;

namespace Beutl.Operators.Configure;

public abstract class ConfigureOperator<TTarget, TValue> : SourceOperator, ISourceTransformer
    where TTarget : IRenderable
    where TValue : CoreObject, IAffectsRender, new()
{
    private bool _transforming;

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

            foreach (TTarget item in value.OfType<TTarget>())
            {
                PreProcess(item, Value);
                Process(item, Value);
                PostProcess(item, Value);
            }
        }
        finally
        {
            _transforming = false;
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

    protected virtual void PreProcess(TTarget target, TValue value)
    {
    }

    protected virtual void PostProcess(TTarget target, TValue value)
    {
    }

    protected abstract void Process(TTarget target, TValue value);

    protected virtual IEnumerable<CoreProperty> GetProperties()
    {
        yield break;
    }
}
