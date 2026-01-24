using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

[Display(Name = nameof(Strings.Delay), ResourceType = typeof(Strings))]
public sealed partial class DelayEffect : AudioEffect
{
    private const float MaxDelayTime = 5000f; // 5 seconds in milliseconds

    public DelayEffect()
    {
        ScanProperties<DelayEffect>();
    }

    [Range(0, MaxDelayTime)]
    [Display(Name = nameof(Strings.DelayTime), ResourceType = typeof(Strings))]
    public IProperty<float> DelayTime { get; } = Property.CreateAnimatable(200f);

    [Range(0, 100)]
    [Display(Name = nameof(Strings.Feedback), ResourceType = typeof(Strings))]
    public IProperty<float> Feedback { get; } = Property.CreateAnimatable(50f);

    [Range(0, 100)]
    [Display(Name = nameof(Strings.DryMix), ResourceType = typeof(Strings))]
    public IProperty<float> DryMix { get; } = Property.CreateAnimatable(60f);

    [Range(0, 100)]
    [Display(Name = nameof(Strings.WetMix), ResourceType = typeof(Strings))]
    public IProperty<float> WetMix { get; } = Property.CreateAnimatable(40f);

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
