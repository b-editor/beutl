using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.LimiterEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class LimiterEffect : AudioEffect
{
    public LimiterEffect()
    {
        ScanProperties<LimiterEffect>();
    }

    [Range(-60f, 0f)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Threshold), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Threshold { get; } = Property.CreateAnimatable(-1.0f);

    [Range(1f, 5000f)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Release), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Release { get; } = Property.CreateAnimatable(50f);

    [Range(0f, 20f)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Lookahead), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Lookahead { get; } = Property.CreateAnimatable(5f);

    [Range(-24f, 24f)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_MakeupGain), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> MakeupGain { get; } = Property.CreateAnimatable(0f);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var limiterNode = context.AddNode(new LimiterNode
        {
            Threshold = Threshold,
            Release = Release,
            Lookahead = Lookahead,
            MakeupGain = MakeupGain
        });

        context.Connect(inputNode, limiterNode);
        return limiterNode;
    }
}
