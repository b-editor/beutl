namespace BEditorNext.Graphics;

public interface IRenderable : IDisposable
{
    public bool IsDisposed { get; }

    public Dictionary<string, object> Options { get; }

    public void Render(IRenderer renderer);
}
