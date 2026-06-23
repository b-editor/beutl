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

    // Enabled children run as a serial cascade (see CreateNode), so their latencies add. Mirrors
    // CreateNode: filters on each child's IsEnabled but does not gate on the group's own IsEnabled —
    // callers decide whether to skip a disabled group (Sound only builds the chain when enabled).
    public override int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        return Children.Where(item => item.IsEnabled)
            .Sum(item => item.GetLatencySamples(sampleRate));
    }
}
