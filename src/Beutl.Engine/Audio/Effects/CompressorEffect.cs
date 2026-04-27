using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(AudioStrings.CompressorEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class CompressorEffect : AudioEffect
{
    public CompressorEffect()
    {
        ScanProperties<CompressorEffect>();
    }

    [Range(-60f, 0f)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Threshold), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Threshold { get; } = Property.CreateAnimatable(-20f);

    [Range(1f, 20f)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Ratio), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Ratio { get; } = Property.CreateAnimatable(4f);

    [Range(0.1f, 500f)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Attack), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Attack { get; } = Property.CreateAnimatable(10f);

    [Range(1f, 5000f)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Release), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Release { get; } = Property.CreateAnimatable(100f);

    [Range(0f, 24f)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_Knee), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Knee { get; } = Property.CreateAnimatable(6f);

    [Range(-24f, 24f)]
    [Display(Name = nameof(AudioStrings.CompressorEffect_MakeupGain), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> MakeupGain { get; } = Property.CreateAnimatable(0f);

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
