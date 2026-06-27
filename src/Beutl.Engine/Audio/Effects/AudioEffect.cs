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
    /// Reports the latency this effect introduces at <paramref name="sampleRate"/>, in samples, so a
    /// host can query it without building a graph node. Report-only; the default 0 covers effects with
    /// no plugin delay. Pass the output (post-resample) sample rate.
    /// </summary>
    /// <remarks>
    /// An override must agree with the <see cref="AudioNode.GetLatencySamples(int)"/> of the node its
    /// <see cref="CreateNode"/> produces, and should return 0 when <c>IsEnabled</c> is false — matching
    /// how <c>Sound.Compose</c> skips a disabled effect's <see cref="CreateNode"/>, so the pre-graph
    /// report matches the graph that gets built.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    public virtual int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return 0;
    }
}
