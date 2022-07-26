using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Styling;

namespace BeUtl.Graphics.Effects;

public sealed class BitmapEffectGroup : BitmapEffect
{
    public static readonly CoreProperty<BitmapEffects> ChildrenProperty;
    private readonly BitmapEffects _children;
    private readonly BitmapProcessorGroup _processor = new();

    static BitmapEffectGroup()
    {
        ChildrenProperty = ConfigureProperty<BitmapEffects, BitmapEffectGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .Register();
        AffectsRender<BitmapEffectGroup>(ChildrenProperty);
    }

    public BitmapEffectGroup()
    {
        _children = new BitmapEffects();
        _children.Attached += item =>
        {
            item.NotifyAttachedToLogicalTree(new(this));
            item.NotifyAttachedToStylingTree(new(this));
        };
        _children.Detached += item =>
        {
            item.NotifyDetachedFromLogicalTree(new(this));
            item.NotifyDetachedFromStylingTree(new(this));
        };
        _children.Invalidated += (_, _) =>
        {
            IBitmapProcessor[] array = new IBitmapProcessor[ValidEffectCount()];
            int index = 0;
            foreach (BitmapEffect item in _children.AsSpan())
            {
                if (item.IsEnabled)
                {
                    array[index] = item.Processor;
                    index++;
                }
            }
            _processor.Processors = array;
            RaiseInvalidated();
        };
    }

    public BitmapEffects Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override IBitmapProcessor Processor => _processor;

    public override Rect TransformBounds(Rect rect)
    {
        foreach (BitmapEffect item in _children.AsSpan())
        {
            if (item.IsEnabled)
                rect = item.TransformBounds(rect);
        }
        return rect;
    }

    private int ValidEffectCount()
    {
        int count = 0;
        foreach (BitmapEffect item in _children.AsSpan())
        {
            if (item.IsEnabled)
            {
                count++;
            }
        }
        return count;
    }

    public override void ApplyStyling(IClock clock)
    {
        base.ApplyStyling(clock);
        foreach (IBitmapEffect item in Children.AsSpan())
        {
            (item as Styleable)?.ApplyStyling(clock);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (IBitmapEffect item in Children.AsSpan())
        {
            (item as Animatable)?.ApplyAnimations(clock);
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("children", out JsonNode? childrenNode)
                && childrenNode is JsonArray childrenArray)
            {
                _children.Clear();
                if (_children.Capacity < childrenArray.Count)
                {
                    _children.Capacity = childrenArray.Count;
                }

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    if (childJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType)
                        && TypeFormat.ToType(atType) is Type type
                        && type.IsAssignableTo(typeof(BitmapEffect))
                        && Activator.CreateInstance(type) is BitmapEffect bitmapEffect)
                    {
                        bitmapEffect.ReadFromJson(childJson);
                        _children.Add(bitmapEffect);
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);

        if (json is JsonObject jobject)
        {
            var array = new JsonArray();

            foreach (BitmapEffect item in _children.AsSpan())
            {
                JsonNode node = new JsonObject();
                item.WriteToJson(ref node);
                node["@type"] = TypeFormat.ToString(item.GetType());

                array.Add(node);
            }

            jobject["children"] = array;
        }
    }
}
