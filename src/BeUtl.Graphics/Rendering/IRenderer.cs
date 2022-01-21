using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.Threading;

namespace BeUtl.Rendering;

public interface IRenderer : IDisposable
{
    ILayerScope? this[int index] { get; set; }

    ICanvas Graphics { get; }

    IClock Clock { get; }

    //public IAudio Audio { get; }

    Dispatcher Dispatcher { get; }

    bool DrawFps { get; set; }

    bool IsDisposed { get; }

    bool IsRendering { get; }

    event EventHandler<RenderResult> RenderInvalidated;

    RenderResult Render();

    void Invalidate();

    public record struct RenderResult(Bitmap<Bgra8888> Bitmap/*, IAudio Audio*/);
}
