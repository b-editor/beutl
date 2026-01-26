using System.ComponentModel.DataAnnotations;

using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

/// <summary>
/// Time stretch audio effect that changes playback speed while preserving pitch.
/// Uses WSOLA (Waveform Similarity Overlap-Add) algorithm.
/// </summary>
[Display(Name = nameof(Strings.TimeStretch), ResourceType = typeof(Strings))]
public sealed partial class TimeStretchEffect : AudioEffect
{
    public TimeStretchEffect()
    {
        ScanProperties<TimeStretchEffect>();
    }

    /// <summary>
    /// Speed percentage (25-400). 100 = normal speed, 200 = 2x speed, 50 = half speed.
    /// </summary>
    [Range(25, 400)]
    [Display(Name = nameof(Strings.Speed), ResourceType = typeof(Strings))]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    public override AudioNode CreateNode(AudioContext context, AudioNode inputNode)
    {
        var node = context.AddNode(new TimeStretchNode
        {
            Speed = Speed
        });

        context.Connect(inputNode, node);
        return node;
    }
}
