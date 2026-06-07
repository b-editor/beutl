using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;
using static Beutl.Audio.Effects.DelayParameters;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.DelayEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class DelayEffect : AudioEffect
{
    public DelayEffect()
    {
        ScanProperties<DelayEffect>();
    }

    [Range(DelayTimeMin, DelayTimeMax)]
    [Display(Name = nameof(AudioStrings.DelayEffect_DelayTime), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> DelayTime { get; } = Property.CreateAnimatable(DelayTimeDefault);

    [Range(FeedbackMin, FeedbackMax)]
    [Display(Name = nameof(AudioStrings.DelayEffect_Feedback), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Feedback { get; } = Property.CreateAnimatable(FeedbackDefault);

    [Range(DryMixMin, DryMixMax)]
    [Display(Name = nameof(AudioStrings.DelayEffect_DryMix), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> DryMix { get; } = Property.CreateAnimatable(DryMixDefault);

    [Range(WetMixMin, WetMixMax)]
    [Display(Name = nameof(AudioStrings.DelayEffect_WetMix), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> WetMix { get; } = Property.CreateAnimatable(WetMixDefault);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var delayNode = context.AddNode(new DelayNode
        {
            DelayTime = DelayTime,
            Feedback = Feedback,
            DryMix = DryMix,
            WetMix = WetMix
        });

        context.Connect(inputNode, delayNode);
        return delayNode;
    }
}
