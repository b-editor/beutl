using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.VibratoEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class VibratoEffect : AudioEffect
{
    internal const float RateMin = 0f;
    internal const float RateMax = 20f;
    internal const float DepthMin = 0f;
    internal const float DepthMax = 20f;

    public VibratoEffect()
    {
        ScanProperties<VibratoEffect>();
    }

    [Range(RateMin, RateMax)]
    [Display(Name = nameof(AudioStrings.VibratoEffect_Rate), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Rate { get; } = Property.CreateAnimatable(5f);

    [Range(DepthMin, DepthMax)]
    [Display(Name = nameof(AudioStrings.VibratoEffect_Depth), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IExpressionProperty<float> Depth { get; } = Property.CreateAnimatable(2f);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var node = context.AddNode(new VibratoNode { Rate = Rate, Depth = Depth });
        context.Connect(inputNode, node);
        return node;
    }
}
