using Beutl.Serialization;

namespace Beutl.Audio.Effects;

public sealed class AudioEffectGroup : AudioEffect
{
    public static readonly CoreProperty<AudioEffects> ChildrenProperty;
    private readonly AudioEffects _children;

    static AudioEffectGroup()
    {
        ChildrenProperty = ConfigureProperty<AudioEffects, AudioEffectGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
        AffectsRender<AudioEffectGroup>(ChildrenProperty);
    }

    public AudioEffectGroup()
    {
        _children = [];
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
    public AudioEffects Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    private int ValidEffectCount()
    {
        int count = 0;
        foreach (IAudioEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
            {
                count++;
            }
        }
        return count;
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<AudioEffects>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override IAudioEffectProcessor CreateProcessor()
    {
        IAudioEffectProcessor[] array = new IAudioEffectProcessor[ValidEffectCount()];
        int index = 0;
        foreach (IAudioEffect item in _children.GetMarshal().Value)
        {
            if (item.IsEnabled)
            {
                array[index] = item.CreateProcessor();
                index++;
            }
        }

        return new AudioEffectProcessorGroup
        {
            Processors = array
        };
    }
}
