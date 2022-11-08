namespace Beutl.Rendering;

public interface IRenderable
{
    bool IsVisible { get; set; }

    bool IsDirty { get; }

    void Invalidate();

    void Render(IRenderer renderer);
}
