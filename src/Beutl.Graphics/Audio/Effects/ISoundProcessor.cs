using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Effects;

public interface ISoundProcessor : IDisposable
{
    void Process(in Pcm<Stereo32BitFloat> src, out Pcm<Stereo32BitFloat> dst);
}
