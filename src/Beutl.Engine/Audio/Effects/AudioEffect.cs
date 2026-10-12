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
    /// without building an audio graph. A disabled effect returns <paramref name="inputLatency"/> unchanged.
    /// </summary>
    /// <param name="sampleRate">The positive sample rate used for both input and output sample counts.</param>
    /// <param name="inputLatency">
    /// The nonnegative accumulated input latency. <see cref="int.MaxValue"/> denotes an unbounded budget.
    /// </param>
    /// <remarks>
    /// An override must agree with <see cref="AudioNode.GetTotalLatencySamples(int)"/> on the node
    /// <see cref="CreateNode"/> produces, and must keep <see cref="int.MaxValue"/> through a time mapping.
    /// </remarks>
    public virtual int GetLatencySamples(int sampleRate, int inputLatency = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegative(inputLatency);
        return inputLatency;
    }
}
