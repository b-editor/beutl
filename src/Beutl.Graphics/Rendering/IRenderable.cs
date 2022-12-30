namespace Beutl.Rendering;

public interface IRenderable
{
    bool IsVisible { get; set; }

    void Invalidate();

    void Render(IRenderer renderer);
}
