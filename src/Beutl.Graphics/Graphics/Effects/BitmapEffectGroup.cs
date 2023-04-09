using System.Text.Json.Nodes;

using Beutl.Animation;

namespace Beutl.Graphics.Effects;

public sealed class BitmapEffectGroup : BitmapEffect
{
    public static readonly CoreProperty<BitmapEffects> ChildrenProperty;
    private readonly BitmapEffects _children;
    private readonly BitmapProcessorGroup _processor = new();

    static BitmapEffectGroup()
    {
        ChildrenProperty = ConfigureProperty<BitmapEffects, BitmapEffectGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
        AffectsRender<BitmapEffectGroup>(ChildrenProperty);
    }

    public BitmapEffectGroup()
    {
        _children = new BitmapEffects();
        _children.Invalidated += (_, e) =>
        {
            IBitmapProcessor[] array = new IBitmapProcessor[ValidEffectCount()];
            int index = 0;
            foreach (IBitmapEffect item in _children.GetMarshal().Value)
            {
                if (item.IsEnabled)
                {
                    array[index] = item.Processor;
                    index++;
                }
            }
            _processor.Processors = array;
            RaiseInvalidated(e);
        };
    }

    [NotAutoSerialized]
    public BitmapEffects Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override IBitmapProcessor Processor => _processor;

    public override Rect TransformBounds(Rect rect)
    {
        foreach (IBitmapEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
                rect = item.TransformBounds(rect);
        }
        return rect;
    }

    private int ValidEffectCount()
    {
        int count = 0;
        foreach (IBitmapEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
            {
                count++;
            }
        }
        return count;
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (IBitmapEffect item in Children.GetMarshal().Value)
        {
            (item as IAnimatable)?.ApplyAnimations(clock);
        }
    }

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
                    && type.IsAssignableTo(typeof(BitmapEffect))
                    && Activator.CreateInstance(type) is IMutableBitmapEffect bitmapEffect)
                {
                    bitmapEffect.ReadFromJson(childJson);
                    _children.Add(bitmapEffect);
                }
            }
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        var array = new JsonArray();

        foreach (IBitmapEffect item in _children.GetMarshal().Value)
        {
            if (item is IMutableBitmapEffect obj)
            {
                var itemJson = new JsonObject();
                obj.WriteToJson(itemJson);
                itemJson.WriteDiscriminator(item.GetType());

                array.Add(itemJson);
            }
        }

        json[nameof(Children)] = array;
    }
}
