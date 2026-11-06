using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.RingModulatorEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class RingModulatorEffect : AudioEffect
{
    internal const float FrequencyMin = 0f;
    internal const float FrequencyMax = 5000f;

    public RingModulatorEffect()
    {
        ScanProperties<RingModulatorEffect>();
    }

    [Range(FrequencyMin, FrequencyMax)]
    [Display(Name = nameof(AudioStrings.RingModulatorEffect_Frequency), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Frequency { get; } = Property.CreateAnimatable(440f);

    [Range(0, 100)]
    [Display(Name = nameof(AudioStrings.RingModulatorEffect_Mix), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Mix { get; } = Property.CreateAnimatable(100f);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var node = context.AddNode(new RingModulatorNode { Frequency = Frequency, Mix = Mix });
        context.Connect(inputNode, node);
        return node;
    }
}
