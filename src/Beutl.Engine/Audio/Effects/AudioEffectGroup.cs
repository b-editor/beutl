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

    // Enabled children run as a serial cascade (CreateNode), so latencies add. A disabled group reports 0
    // to match Sound.Compose skipping its CreateNode, keeping the report aligned with the built graph.
    public override int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (!IsEnabled)
            return 0;

        long total = 0;
        foreach (AudioEffect item in Children)
        {
            if (!item.IsEnabled)
                continue;

            int latency = item.GetLatencySamples(sampleRate);
            if (latency < 0)
            {
                throw new InvalidOperationException(
                    $"{item.GetType().Name} reported a negative latency ({latency} samples).");
            }

            if (latency == int.MaxValue)
                return int.MaxValue;

            total += latency;
            if (total >= int.MaxValue)
                return int.MaxValue;
        }

        return (int)total;
    }
}
