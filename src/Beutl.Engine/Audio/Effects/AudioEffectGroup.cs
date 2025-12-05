using Beutl.Audio.Graph;
using Beutl.Engine;

namespace Beutl.Audio.Effects;

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
}
