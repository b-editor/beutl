using System.ComponentModel.DataAnnotations;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Nodes;
using Beutl.Engine;
using Beutl.Language;

using static Beutl.Audio.Effects.LimiterParameters;

namespace Beutl.Audio.Effects;

/// <summary>
/// Brick-wall peak limiter effect with lookahead. Prevents the output peak
/// amplitude from exceeding <see cref="Threshold"/> using instant attack and
/// exponential (one-pole IIR) release.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Lookahead"/> above 0 ms delays the whole signal by that amount so gain
/// reduction can precede a peak. Beutl's audio graph runs effects inline (output length
/// equals input length) with no latency compensation, so at a contiguous-run boundary
/// (clip end, seek, loop, edit) the limiter resets and the buffered tail is dropped. The
/// default 0 ms keeps audio sample-accurate and A/V-synchronized; raise it to trade a
/// fixed delay for better transient transparency.
/// </para>
/// <para>
/// A master limiter (<see cref="Beutl.Audio.Composing.Composer"/>) always re-limits the
/// mixed bus after this effect, so the brick-wall ceiling configured here holds end-to-end
/// only when the summed bus stays at or below 0 dBFS — multiple sounds sum on the master
/// bus, so that can be exceeded even when this limiter's
/// <see cref="Threshold"/> + <see cref="MakeupGain"/> is within 0 dBFS.
/// </para>
/// </remarks>
[Display(Name = nameof(AudioStrings.LimiterEffect), ResourceType = typeof(AudioStrings))]
public sealed partial class LimiterEffect : AudioEffect
{
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
    [Display(Name = nameof(AudioStrings.LimiterEffect_Threshold), Description = nameof(AudioStrings.LimiterEffect_Threshold_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(10, 1)]
    public IProperty<float> Threshold { get; } = Property.CreateAnimatable(DefaultThresholdDb);

    /// <summary>
    /// Time constant of the gain recovery (one-pole IIR) in milliseconds.
    /// </summary>
    [Range(MinReleaseMs, MaxReleaseMs)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Release), Description = nameof(AudioStrings.LimiterEffect_Release_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(100, 10)]
    public IProperty<float> Release { get; } = Property.CreateAnimatable(DefaultReleaseMs);

    /// <summary>
    /// Lookahead window in milliseconds. The output is delayed by this amount
    /// so that gain reduction can begin before a peak arrives. See the type-level
    /// remarks for the latency trade-off — non-zero values shift the clip later
    /// and drop the same number of samples from the tail.
    /// </summary>
    [Range(MinLookaheadMs, MaxLookaheadMs)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_Lookahead), Description = nameof(AudioStrings.LimiterEffect_Lookahead_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(5, 0.5)]
    public IProperty<float> Lookahead { get; } = Property.CreateAnimatable(DefaultLookaheadMs);

    /// <summary>
    /// Gain applied to the output after the limiter stage, in decibels. The
    /// final peak can therefore reach <c>Threshold + MakeupGain</c> dB.
    /// </summary>
    [Range(MinMakeupGainDb, MaxMakeupGainDb)]
    [Display(Name = nameof(AudioStrings.LimiterEffect_MakeupGain), Description = nameof(AudioStrings.LimiterEffect_MakeupGain_Description), ResourceType = typeof(AudioStrings))]
    [SuppressResourceClassGeneration]
    [NumberStep(1, 0.1)]
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

    public override int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (!IsEnabled)
            return 0;

        // Match LimiterNode: an animated lookahead reports the worst case so a host querying before
        // graph construction reserves the same room the node would.
        return Lookahead.Animation != null
            ? ToLatencySamples(MaxLookaheadMs, sampleRate)
            : ToLatencySamples(Lookahead.CurrentValue, sampleRate);
    }
}
