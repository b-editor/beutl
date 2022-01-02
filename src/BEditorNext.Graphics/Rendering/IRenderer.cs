using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;
using BEditorNext.Threading;

namespace BEditorNext.Rendering;

public interface IRenderer : IDisposable
{
    ICanvas Graphics { get; }

    //public IAudio Audio { get; }

    Dispatcher Dispatcher { get; }

    bool IsDisposed { get; }

    bool IsRendering { get; }

    event EventHandler<RenderResult> RenderInvalidated;

    RenderResult Render();

    void Invalidate();

    public record struct RenderResult(Bitmap<Bgra8888> Bitmap/*, IAudio Audio*/);
}
