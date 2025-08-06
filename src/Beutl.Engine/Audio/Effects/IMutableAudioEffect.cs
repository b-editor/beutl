using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Effects;

public interface IMutableAudioEffect : IAudioEffect, ICoreObject, IAffectsRender, IAnimatable
{
}
