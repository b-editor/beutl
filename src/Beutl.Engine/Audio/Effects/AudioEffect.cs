using Beutl.Animation;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Audio.Effects;

public abstract partial class AudioEffect : EngineObject
{
    public abstract IAudioEffectProcessor CreateProcessor();
}
