using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.PitchShiftEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class PitchShiftEffect : AudioEffect
{
    internal const float SemitonesMin = -24f;
    internal const float SemitonesMax = 24f;

    public PitchShiftEffect()
    {
        ScanProperties<PitchShiftEffect>();
    }

    [Range(SemitonesMin, SemitonesMax)]
    [Display(Name = nameof(AudioStrings.PitchShiftEffect_Semitones), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Semitones { get; } = Property.CreateAnimatable(0f);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var node = context.AddNode(new PitchShiftNode { Semitones = Semitones, ChannelCount = context.ChannelCount });
        context.Connect(inputNode, node);
        return node;
    }
}
