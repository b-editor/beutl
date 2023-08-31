using Beutl.Animation;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Rendering;

public interface IComposer : IDisposable
{
    IClock Clock { get; }

    bool IsAudioRendering { get; }

    bool IsDisposed { get; }

    int SampleRate { get; }

    Pcm<Stereo32BitFloat>? Compose(TimeSpan timeSpan);
}
