using Beutl.Serialization;

namespace Beutl.Audio.Effects;

public sealed partial class AudioEffectGroup : AudioEffect
{
    public AudioEffectGroup()
    {
        Children = new AudioEffects(this);
        Children.Invalidated += (_, e) => RaiseInvalidated(e);
        ScanProperties<AudioEffectGroup>();
    }

    public AudioEffects Children { get; }

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
