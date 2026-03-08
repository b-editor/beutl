using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.Audio.Effects;

public sealed partial class FallbackAudioEffect : AudioEffect, IFallback;

[FallbackType(typeof(FallbackAudioEffect))]
public abstract partial class AudioEffect : EngineObject
{
    public abstract AudioNode CreateNode(AudioContext context, AudioNode inputNode);
}
