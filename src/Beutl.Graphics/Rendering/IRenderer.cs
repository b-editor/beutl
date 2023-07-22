using Beutl.Animation;
using Beutl.Audio;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.Threading;

namespace Beutl.Rendering;

public interface IRenderer : IDisposable
{
    RenderScene RenderScene { get; }

    ImmediateCanvas Canvas { get; }

    Audio.Audio Audio { get; }

    IClock Clock { get; }

    Dispatcher Dispatcher { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsGraphicsRendering { get; }
    
    bool IsAudioRendering { get; }

    event EventHandler<TimeSpan> RenderInvalidated;

    RenderResult RenderGraphics(TimeSpan timeSpan);

    RenderResult RenderAudio(TimeSpan timeSpan);

    RenderResult Render(TimeSpan timeSpan);

    void RaiseInvalidated(TimeSpan timeSpan);

    //void AddDirty(IRenderable renderable);
    
    //void AddDirtyRect(Rect rect);

    //void AddDirtyRange(TimeRange timeRange);

    public record struct RenderResult(Bitmap<Bgra8888>? Bitmap = null, Pcm<Stereo32BitFloat>? Audio = null);
}
