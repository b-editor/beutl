using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Audio.Effects;

public sealed partial class FallbackAudioEffect : AudioEffect, IFallback;

[FallbackType(typeof(FallbackAudioEffect))]
public abstract partial class AudioEffect : EngineObject
{
    public abstract AudioNode CreateNode(AudioContext context, AudioNode inputNode);

    /// <summary>
    /// Reports the accumulated latency at this effect's output, including <paramref name="inputLatency"/>,
    /// without building an audio graph. The default implementation passes the input latency through.
    /// </summary>
    /// <param name="sampleRate">The positive sample rate used for both input and output sample counts.</param>
    /// <param name="inputLatency">
    /// The nonnegative accumulated latency in the effect's input timeline. Defaults to 0 to query the
    /// effect's own contribution. <see cref="int.MaxValue"/> denotes an unbounded or saturated budget.
    /// </param>
    /// <returns>The accumulated latency in the effect's output timeline, in samples at <paramref name="sampleRate"/>.</returns>
    /// <remarks>
    /// <para>
    /// An override must agree with <see cref="AudioNode.GetTotalLatencySamples(int)"/> on the node its
    /// <see cref="CreateNode"/> produces when the input node reports <paramref name="inputLatency"/>.
    /// Additive effects add their own delay using saturating arithmetic. Effects that change playback
    /// speed transform the input budget into their output timeline, using conservative bounds for
    /// animated parameters. Serial groups fold the accumulated budget through enabled children in order.
    /// </para>
    /// <para>
    /// Validate both arguments even when disabled. A disabled effect returns <paramref name="inputLatency"/>
    /// unchanged, matching how <c>Sound.Compose</c> skips its <see cref="CreateNode"/>. Valid results are in
    /// the inclusive range 0..<see cref="int.MaxValue"/>. Preserve <see cref="int.MaxValue"/> even when a
    /// time mapping would otherwise reduce it. This report does not change audio processing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate"/> is not positive or <paramref name="inputLatency"/> is negative.
    /// </exception>
    public virtual int GetLatencySamples(int sampleRate, int inputLatency = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(inputLatency);
        return inputLatency;
    }
}
