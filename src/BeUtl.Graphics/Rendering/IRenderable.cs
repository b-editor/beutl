namespace BeUtl.Rendering;

public interface IRenderable : IDisposable
{
    bool IsDisposed { get; }

    bool IsVisible { get; set; }

    bool IsDirty { get; }

    void Render(IRenderer renderer);
}
