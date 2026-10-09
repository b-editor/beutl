using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.AudioEffectGroup), ResourceType = typeof(AudioStrings))]
public sealed partial class AudioEffectGroup : AudioEffect
{
    public AudioEffectGroup()
    {
        ScanProperties<AudioEffectGroup>();
    }

    public IListProperty<AudioEffect> Children { get; } = Property.CreateList<AudioEffect>();

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        return Children.Where(item => item.IsEnabled)
            .Aggregate(inputNode, (current, item) => item.CreateNode(context, current));
    }

    // Report the same enabled serial cascade that CreateNode builds.
    public override int GetLatencySamples(int sampleRate, int inputLatency = 0)
    {
        base.GetLatencySamples(sampleRate, inputLatency);
        if (!IsEnabled)
            return inputLatency;

        foreach (AudioEffect item in Children.Where(item => item.IsEnabled))
        {
            int latency = item.GetLatencySamples(sampleRate, inputLatency);
            if (latency < 0)
                throw new InvalidOperationException($"{item.GetType().Name} reported a negative latency ({latency} samples).");

            inputLatency = latency;
        }

        return inputLatency;
    }
}
