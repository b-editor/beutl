using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Audio.Effects;

/// <summary>
/// Brick-wall peak limiter effect with lookahead. Prevents the output peak
/// amplitude from exceeding <see cref="Threshold"/> using instant attack and
/// exponential (one-pole IIR) release.
/// </summary>
/// <remarks>
/// The DSP layer (<see cref="Beutl.Audio.Graph.Nodes.LimiterNode"/>) re-clamps
/// every parameter to the ranges declared here, so the constants double as the
/// authoritative range and as the safety net for animations or restored project
/// files that bypass the <see cref="RangeAttribute"/> validation.
/// </remarks>
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

    internal const float DefaultThresholdDb = -1.0f;
    internal const float DefaultReleaseMs = 50f;
    internal const float DefaultLookaheadMs = 5f;
    internal const float DefaultMakeupGainDb = 0f;

    public LimiterEffect()
    {
        ScanProperties<LimiterEffect>();
    }

    /// <summary>
    /// Output ceiling in decibels. The peak detector evaluates the maximum
    /// absolute sample value within the lookahead window; whenever it exceeds
    /// this threshold the gain is reduced so the delayed output stays at or
    /// below this ceiling.
    /// </summary>
    [Range(MinThresholdDb, MaxThresholdDb)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Threshold), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Threshold { get; } = Property.CreateAnimatable(DefaultThresholdDb);

    /// <summary>
    /// Time constant of the gain recovery (one-pole IIR) in milliseconds.
    /// </summary>
    [Range(MinReleaseMs, MaxReleaseMs)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Release), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Release { get; } = Property.CreateAnimatable(DefaultReleaseMs);

    /// <summary>
    /// Lookahead window in milliseconds. The output is delayed by this amount
    /// so that gain reduction can begin before a peak arrives.
    /// </summary>
    [Range(MinLookaheadMs, MaxLookaheadMs)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Lookahead), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> Lookahead { get; } = Property.CreateAnimatable(DefaultLookaheadMs);

    /// <summary>
    /// Gain applied to the output after the limiter stage, in decibels. The
    /// final peak can therefore reach <c>Threshold + MakeupGain</c> dB.
    /// </summary>
    [Range(MinMakeupGainDb, MaxMakeupGainDb)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_MakeupGain), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    public IProperty<float> MakeupGain { get; } = Property.CreateAnimatable(DefaultMakeupGainDb);

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
