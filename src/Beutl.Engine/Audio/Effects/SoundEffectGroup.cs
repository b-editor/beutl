using System.Text.Json.Nodes;

using Beutl.Serialization;

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
        _children = [];
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

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if(context.GetValue<SoundEffects>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
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
