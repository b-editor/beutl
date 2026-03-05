using Beutl.Composition;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public interface IComposer : IDisposable
{
    bool IsAudioRendering { get; }

    bool IsDisposed { get; }

    int SampleRate { get; }

    AudioBuffer? Compose(TimeRange range, CompositionFrame frame);
}
