using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;
using static Beutl.Audio.Effects.TimeStretchParameters;

namespace Beutl.Audio.Effects;

/// <summary>Changes playback speed while preserving the original pitch.</summary>
[Display(Name = nameof(AudioStrings.TimeStretchEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class TimeStretchEffect : AudioEffect
{
    public TimeStretchEffect()
    {
        ScanProperties<TimeStretchEffect>();
    }

    [Range(MinSpeed, MaxSpeed)]
    [Display(Name = nameof(AudioStrings.TimeStretchEffect_Speed),
        Description = nameof(AudioStrings.TimeStretchEffect_Speed_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(DefaultSpeed);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var node = context.CreateNode(
            Speed,
            static speed => new SpeedNode { Speed = speed, PreservePitch = true },
            static (speed, existing) => existing.Speed = speed,
            static (speed, existing) => existing.PreservePitch && ReferenceEquals(existing.Speed, speed));
        context.Connect(inputNode, node);
        return node;
    }

    internal override int GetOutputLatencySamples(int sampleRate, int inputLatency)
    {
        if (!IsEnabled || inputLatency == 0 || inputLatency == int.MaxValue)
            return inputLatency;

        double scaled = Math.Ceiling(inputLatency / GetMinimumSpeedFactor(Speed));
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }
}
