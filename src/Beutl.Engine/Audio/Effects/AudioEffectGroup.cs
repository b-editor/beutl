using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Audio.Effects;

public sealed partial class AudioEffectGroup : AudioEffect
{
    public AudioEffectGroup()
    {
        ScanProperties<AudioEffectGroup>();
    }

    public IListProperty<AudioEffect> Children { get; } = Property.CreateList<AudioEffect>();

    private int ValidEffectCount()
    {
        int count = 0;
        foreach (AudioEffect item in Children)
        {
            if (item.IsEnabled)
            {
                count++;
            }
        }
        return count;
    }

    public override IAudioEffectProcessor CreateProcessor()
    {
        IAudioEffectProcessor[] array = new IAudioEffectProcessor[ValidEffectCount()];
        int index = 0;
        foreach (AudioEffect item in Children)
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
