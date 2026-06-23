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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    public virtual int GetLatencySamples(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return 0;
    }
}
