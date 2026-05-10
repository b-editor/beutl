using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

/// <summary>
/// Brick-wall peak limiter effect with lookahead. Prevents the output peak
/// amplitude from exceeding <see cref="Threshold"/> by applying instant gain
/// reduction with an IIR-shaped release.
/// </summary>
[Display(Name = nameof(AudioStrings.LimiterEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class LimiterEffect : AudioEffect
{
    internal const float MinThresholdDb = -60f;
    internal const float MaxThresholdDb = 0f;
    internal const float MinReleaseMs = 1f;
    internal const float MaxReleaseMs = 5000f;
    internal const float MinLookaheadMs = 0f;
    internal const float MaxLookaheadMs = 20f;
    internal const float MinMakeupGainDb = -24f;
    internal const float MaxMakeupGainDb = 24f;

    public LimiterEffect()
    {
        ScanProperties<LimiterEffect>();
    }

    /// <summary>
    /// Output ceiling in decibels. The peak detector evaluates the maximum
    /// absolute sample value within the lookahead window; whenever it exceeds
    /// this threshold the gain is reduced so the delayed output stays at or
    /// below this ceiling. Range: -60 dB to 0 dB.
    /// </summary>
    [Range(MinThresholdDb, MaxThresholdDb)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Threshold), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Threshold { get; } = Property.CreateAnimatable(-1.0f);

    /// <summary>
    /// Time constant of the gain recovery (one-pole IIR) in milliseconds.
    /// Range: 1 ms to 5000 ms.
    /// </summary>
    [Range(MinReleaseMs, MaxReleaseMs)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Release), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Release { get; } = Property.CreateAnimatable(50f);

    /// <summary>
    /// Lookahead window in milliseconds. The output is delayed by this amount
    /// so that gain reduction can begin before a peak arrives. Range: 0 ms to 20 ms.
    /// </summary>
    [Range(MinLookaheadMs, MaxLookaheadMs)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Lookahead), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Lookahead { get; } = Property.CreateAnimatable(5f);

    /// <summary>
    /// Gain applied to the output after limiting, in decibels. Note that
    /// makeup gain can push the output back above the threshold.
    /// Range: -24 dB to +24 dB.
    /// </summary>
    [Range(MinMakeupGainDb, MaxMakeupGainDb)]
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
