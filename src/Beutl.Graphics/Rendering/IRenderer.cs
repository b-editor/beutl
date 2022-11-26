using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Threading;

namespace Beutl.Rendering;

// RenderingのApiで時間を考慮する
// Renderable内で持続時間と開始時間のプロパティを追加
public interface IRenderer : IDisposable
{
    ILayerContext? this[int index] { get; set; }

    ICanvas Graphics { get; }

    IClock Clock { get; }

    //public IAudio Audio { get; }

    Dispatcher Dispatcher { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsRendering { get; }

    event EventHandler<RenderResult> RenderInvalidated;

    RenderResult RenderGraphics(TimeSpan timeSpan);

    RenderResult RenderAudio(TimeSpan timeSpan);

    RenderResult Render(TimeSpan timeSpan);

    void Invalidate(TimeSpan timeSpan);

    void AddDirtyRect(Rect rect);

    public record struct RenderResult(Bitmap<Bgra8888> Bitmap/*, IAudio Audio*/);
}
