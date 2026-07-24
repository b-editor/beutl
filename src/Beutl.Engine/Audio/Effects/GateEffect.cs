using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

using static Beutl.Audio.Effects.GateParameters;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.GateEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class GateEffect : AudioEffect
{
    public GateEffect()
    {
        ScanProperties<GateEffect>();
    }

    [Range(MinThresholdDb, MaxThresholdDb)]
    [Display(Name = nameof(AudioStrings.GateEffect_Threshold), Description = nameof(AudioStrings.GateEffect_Threshold_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IProperty<float> Threshold { get; } = Property.CreateAnimatable(DefaultThresholdDb);

    [Range(MinAttackMs, MaxAttackMs)]
    [Display(Name = nameof(AudioStrings.GateEffect_Attack), Description = nameof(AudioStrings.GateEffect_Attack_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IProperty<float> Attack { get; } = Property.CreateAnimatable(DefaultAttackMs);

    [Range(MinHoldMs, MaxHoldMs)]
    [Display(Name = nameof(AudioStrings.GateEffect_Hold), Description = nameof(AudioStrings.GateEffect_Hold_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(100, 10)]
    public IProperty<float> Hold { get; } = Property.CreateAnimatable(DefaultHoldMs);

    [Range(MinReleaseMs, MaxReleaseMs)]
    [Display(Name = nameof(AudioStrings.GateEffect_Release), Description = nameof(AudioStrings.GateEffect_Release_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(100, 10)]
    public IProperty<float> Release { get; } = Property.CreateAnimatable(DefaultReleaseMs);

    [Range(MinRangeDb, MaxRangeDb)]
    [Display(Name = nameof(AudioStrings.GateEffect_Range), Description = nameof(AudioStrings.GateEffect_Range_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IProperty<float> Range { get; } = Property.CreateAnimatable(DefaultRangeDb);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var gateNode = context.AddNode(new GateNode
        {
            Threshold = Threshold,
            Attack = Attack,
            Hold = Hold,
            Release = Release,
            Range = Range
        });

        context.Connect(inputNode, gateNode);
        return gateNode;
    }
}
