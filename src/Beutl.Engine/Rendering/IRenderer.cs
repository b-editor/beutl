using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;

namespace Beutl.Rendering;

public interface IRenderer : IDisposable, IImmediateCanvasFactory
{
    RenderScene RenderScene { get; }

    PixelSize FrameSize { get; }

    int SampleRate { get; }

    IClock Clock { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }

    bool IsAudioRendering { get; }

    event EventHandler<TimeSpan> RenderInvalidated;

    RenderResult RenderGraphics(TimeSpan timeSpan);

    RenderResult RenderAudio(TimeSpan timeSpan);

    RenderResult Render(TimeSpan timeSpan);

    void RaiseInvalidated(TimeSpan timeSpan);

    public record struct RenderResult(Bitmap<Bgra8888>? Bitmap = null, Pcm<Stereo32BitFloat>? Audio = null);
}
