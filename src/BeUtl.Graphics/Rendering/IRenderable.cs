namespace BeUtl.Rendering;

public interface IRenderable
{
    bool IsVisible { get; set; }

    bool IsDirty { get; }

    void Render(IRenderer renderer);
}
