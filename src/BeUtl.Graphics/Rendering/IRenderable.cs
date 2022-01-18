namespace BeUtl.Rendering;

public interface IRenderable : IDisposable
{
    bool IsDisposed { get; }

    bool IsVisible { get; set; }

    void Render(IRenderer renderer);
}
