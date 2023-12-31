using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed class FilterEffectGroup : FilterEffect
{
    public static readonly CoreProperty<FilterEffects> ChildrenProperty;
    private readonly FilterEffects _children;

    static FilterEffectGroup()
    {
        ChildrenProperty = ConfigureProperty<FilterEffects, FilterEffectGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
    }

    public FilterEffectGroup()
    {
        _children = [];
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
    public FilterEffects Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue(nameof(Children), out JsonNode? childrenNode)
            && childrenNode is JsonArray childrenArray)
        {
            _children.Clear();
            _children.EnsureCapacity(childrenArray.Count);

            foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
            {
                if (childJson.TryGetDiscriminator(out Type? type)
                    && type.IsAssignableTo(typeof(FilterEffect))
                    && Activator.CreateInstance(type) is FilterEffect imageFilter)
                {
                    imageFilter.ReadFromJson(childJson);
                    _children.Add(imageFilter);
                }
            }
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        var array = new JsonArray();

        foreach (FilterEffect item in _children.GetMarshal().Value)
        {
            var itemJson = new JsonObject();
            item.WriteToJson(itemJson);
            itemJson.WriteDiscriminator(item.GetType());

            array.Add(itemJson);
        }

        json[nameof(Children)] = array;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if(context.GetValue<FilterEffects>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (FilterEffect item in Children.GetMarshal().Value)
        {
            (item as IAnimatable)?.ApplyAnimations(clock);
        }
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        foreach (FilterEffect item in _children.GetMarshal().Value)
        {
            context.Apply(item);
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        foreach (FilterEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
                bounds = item.TransformBounds(bounds);
        }

        return bounds;
    }
}
