using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(Strings.Group), ResourceType = typeof(Strings))]
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
