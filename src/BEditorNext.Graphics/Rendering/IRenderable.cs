namespace BEditorNext.Rendering;

public interface IRenderable : IDisposable
{
    public bool IsDisposed { get; }

    public void Render(IRenderer renderer);
}
