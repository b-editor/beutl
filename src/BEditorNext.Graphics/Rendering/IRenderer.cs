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

    event EventHandler<RenderResult> RenderRequested;

    RenderResult Render();

    public record struct RenderResult(Bitmap<Bgra8888> Bitmap/*, IAudio Audio*/);
}
