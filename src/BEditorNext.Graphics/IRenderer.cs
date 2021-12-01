using BEditorNext.Graphics.Pixel;

namespace BEditorNext.Graphics;

public interface IRenderer : IDisposable
{
    public IGraphics Graphics { get; }

    //public IAudio Audio { get; }

    public bool IsDisposed { get; }

    public bool IsRendering { get; }

    public RenderResult Render();

    public record struct RenderResult(Bitmap<Bgra8888> Bitmap/*, IAudio Audio*/);
}
