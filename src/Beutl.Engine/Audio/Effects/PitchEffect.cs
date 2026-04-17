using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.Pitch), ResourceType = typeof(AudioStrings))]
public sealed partial class PitchEffect : AudioEffect
{
    public PitchEffect()
    {
        ScanProperties<PitchEffect>();
    }

    [Range(-24, 24)]
    [Display(Name = nameof(AudioStrings.PitchSemitones), ResourceType = typeof(AudioStrings))]
    public IProperty<float> Semitones { get; } = Property.CreateAnimatable(0f);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var pitchNode = context.AddNode(new PitchNode
        {
            Semitones = Semitones
        });

        context.Connect(inputNode, pitchNode);
        return pitchNode;
    }
}
