using System.Text.Json.Nodes;

namespace Beutl.Audio.Effects;

public sealed class SoundEffectGroup : SoundEffect
{
    public static readonly CoreProperty<SoundEffects> ChildrenProperty;
    private readonly SoundEffects _children;

    static SoundEffectGroup()
    {
        ChildrenProperty = ConfigureProperty<SoundEffects, SoundEffectGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
        AffectsRender<SoundEffectGroup>(ChildrenProperty);
    }

    public SoundEffectGroup()
    {
        _children = new SoundEffects();
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
    public SoundEffects Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    private int ValidEffectCount()
    {
        int count = 0;
        foreach (ISoundEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
            {
                count++;
            }
        }
        return count;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue(nameof(Children), out JsonNode? childrenNode)
                && childrenNode is JsonArray childrenArray)
            {
                _children.Clear();
                _children.EnsureCapacity(childrenArray.Count);

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    if (childJson.TryGetDiscriminator(out Type? type)
                        && type.IsAssignableTo(typeof(SoundEffect))
                        && Activator.CreateInstance(type) is IMutableSoundEffect soundEffect)
                    {
                        soundEffect.ReadFromJson(childJson);
                        _children.Add(soundEffect);
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

            foreach (ISoundEffect item in _children.GetMarshal().Value)
            {
                if (item is IMutableSoundEffect obj)
                {
                    JsonNode node = new JsonObject();
                    obj.WriteToJson(ref node);
                    node.WriteDiscriminator(item.GetType());

                    array.Add(node);
                }
            }

            jobject[nameof(Children)] = array;
        }
    }

    public override ISoundProcessor CreateProcessor()
    {
        ISoundProcessor[] array = new ISoundProcessor[ValidEffectCount()];
        int index = 0;
        foreach (ISoundEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
            {
                array[index] = item.CreateProcessor();
                index++;
            }
        }

        return new SoundProcessorGroup
        {
            Processors = array
        };
    }
}
