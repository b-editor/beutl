using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

using static Beutl.Audio.Effects.CompressorParameters;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.CompressorEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class CompressorEffect : AudioEffect
{
    public CompressorEffect()
    {
        ScanProperties<CompressorEffect>();
    }

    [Range(MinThresholdDb, MaxThresholdDb)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Threshold), Description = nameof(AudioStrings.CompressorEffect_Threshold_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IProperty<float> Threshold { get; } = Property.CreateAnimatable(DefaultThresholdDb);

    [Range(MinRatio, MaxRatio)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Ratio), Description = nameof(AudioStrings.CompressorEffect_Ratio_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(1, 0.1)]
    public IProperty<float> Ratio { get; } = Property.CreateAnimatable(DefaultRatio);

    [Range(MinAttackMs, MaxAttackMs)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Attack), Description = nameof(AudioStrings.CompressorEffect_Attack_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IProperty<float> Attack { get; } = Property.CreateAnimatable(DefaultAttackMs);

    [Range(MinReleaseMs, MaxReleaseMs)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Release), Description = nameof(AudioStrings.CompressorEffect_Release_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(100, 10)]
    public IProperty<float> Release { get; } = Property.CreateAnimatable(DefaultReleaseMs);

    [Range(MinKneeDb, MaxKneeDb)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Knee), Description = nameof(AudioStrings.CompressorEffect_Knee_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(1, 0.1)]
    public IProperty<float> Knee { get; } = Property.CreateAnimatable(DefaultKneeDb);

    [Range(MinMakeupGainDb, MaxMakeupGainDb)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_MakeupGain), Description = nameof(AudioStrings.CompressorEffect_MakeupGain_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(1, 0.1)]
    public IProperty<float> MakeupGain { get; } = Property.CreateAnimatable(DefaultMakeupGainDb);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var compressorNode = context.AddNode(new CompressorNode
        {
            Threshold = Threshold,
            Ratio = Ratio,
            Attack = Attack,
            Release = Release,
            Knee = Knee,
            MakeupGain = MakeupGain
        });

        context.Connect(inputNode, compressorNode);
        return compressorNode;
    }
}
