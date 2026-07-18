using Beutl.Composition;
using Beutl.Media;

namespace Beutl.Audio.Composing;

public interface IComposer : IDisposable
{
    /// <summary>The preview/delivery policy accepted by this composer.</summary>
    Beutl.Graphics.Rendering.RenderIntent RenderIntent
        => Beutl.Graphics.Rendering.RenderIntent.Preview;

    bool IsAudioRendering { get; }

    bool IsDisposed { get; }

    int SampleRate { get; }

    AudioBuffer? Compose(TimeRange range, CompositionFrame frame);
}
