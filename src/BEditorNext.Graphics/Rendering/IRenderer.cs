using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Rendering;

public interface IRenderer : IDisposable
{
    public IGraphics Graphics { get; }

    //public IAudio Audio { get; }

    public bool IsDisposed { get; }

    public bool IsRendering { get; }

    public event EventHandler<RenderResult> RenderRequested;

    public RenderResult Render();

    public record struct RenderResult(Bitmap<Bgra8888> Bitmap/*, IAudio Audio*/);
}
