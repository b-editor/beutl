using Beutl.Animation;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Audio.Composing;

public interface IComposer : IDisposable
{
    bool IsAudioRendering { get; }

    bool IsDisposed { get; }

    int SampleRate { get; }

    AudioBuffer? Compose(TimeRange range);
}
