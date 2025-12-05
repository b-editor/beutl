using Beutl.Audio.Graph;
using Beutl.Engine;

namespace Beutl.Audio.Effects;

public abstract partial class AudioEffect : EngineObject
{
    public abstract AudioNode CreateNode(AudioContext context, AudioNode inputNode);
}
